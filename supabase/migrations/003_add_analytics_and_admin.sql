-- ==============================================================================
-- GitJumps Analytics & Admin Dashboard — Migration 003
-- ==============================================================================

-- 1. Gameplay Activity Events Table
CREATE TABLE IF NOT EXISTS public.gameplay_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id UUID NOT NULL REFERENCES public.leaderboard_players(id) ON DELETE CASCADE,
    event_name TEXT NOT NULL,
    session_id UUID,
    score INTEGER,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_event_name CHECK (event_name IN ('game_started', 'game_over', 'score_submitted', 'personal_best_achieved'))
);

CREATE INDEX IF NOT EXISTS idx_gameplay_events_player_created 
    ON public.gameplay_events (player_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_gameplay_events_event_created 
    ON public.gameplay_events (event_name, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_gameplay_events_created_at 
    ON public.gameplay_events (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_gameplay_events_session_id 
    ON public.gameplay_events (session_id) 
    WHERE session_id IS NOT NULL;

-- Enable Row Level Security on gameplay_events
ALTER TABLE public.gameplay_events ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.gameplay_events FROM anon, authenticated;
GRANT ALL ON TABLE public.gameplay_events TO service_role, postgres;


-- 2. Add Activity and Retention Tracking Columns to leaderboard_players
ALTER TABLE public.leaderboard_players
    ADD COLUMN IF NOT EXISTS first_game_started_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS last_active_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS total_sessions INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_runs INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_leaderboard_players_last_active 
    ON public.leaderboard_players (last_active_at DESC);

CREATE INDEX IF NOT EXISTS idx_leaderboard_players_created_at 
    ON public.leaderboard_players (created_at DESC);

-- Backfill existing players who already have recorded activity
UPDATE public.leaderboard_players
SET first_game_started_at = COALESCE(first_game_started_at, created_at),
    last_active_at = COALESCE(last_active_at, updated_at),
    total_runs = CASE WHEN best_score > 0 AND total_runs = 0 THEN 1 ELSE total_runs END,
    total_sessions = CASE WHEN best_score > 0 AND total_sessions = 0 THEN 1 ELSE total_sessions END
WHERE has_submitted_score = TRUE;


-- 3. Admin Users Table and Authorization
CREATE TABLE IF NOT EXISTS public.admin_users (
    id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email TEXT NOT NULL UNIQUE,
    role TEXT NOT NULL DEFAULT 'admin',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE public.admin_users ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.admin_users FROM anon;
GRANT SELECT ON TABLE public.admin_users TO authenticated;
GRANT ALL ON TABLE public.admin_users TO service_role, postgres;

-- Helper function to check if the current user is an authorized admin
CREATE OR REPLACE FUNCTION public.is_admin()
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT EXISTS (
        SELECT 1 FROM public.admin_users
        WHERE id = auth.uid()
    );
$$;

DROP POLICY IF EXISTS "Admins can view admin_users" ON public.admin_users;
CREATE POLICY "Admins can view admin_users"
    ON public.admin_users
    FOR SELECT
    TO authenticated
    USING (public.is_admin());

GRANT SELECT ON TABLE public.gameplay_events TO authenticated;

DROP POLICY IF EXISTS "Admins can view gameplay_events" ON public.gameplay_events;
CREATE POLICY "Admins can view gameplay_events"
    ON public.gameplay_events
    FOR SELECT
    TO authenticated
    USING (public.is_admin());


-- 4. RPC: Track Gameplay Event (Called by backend Edge Functions or authenticated client)
CREATE OR REPLACE FUNCTION public.track_gameplay_event(
    p_telegram_id BIGINT,
    p_event_name TEXT,
    p_session_id UUID DEFAULT NULL,
    p_score INTEGER DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'::jsonb
)
RETURNS TABLE (
    event_id UUID,
    player_id UUID,
    player_code VARCHAR(7)
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_player public.leaderboard_players%ROWTYPE;
    v_event_id UUID;
BEGIN
    SELECT * INTO v_player
    FROM public.leaderboard_players
    WHERE telegram_id = p_telegram_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'PLAYER_NOT_FOUND: Player with Telegram ID % does not exist.', p_telegram_id;
    END IF;

    -- Update player activity metrics
    UPDATE public.leaderboard_players
    SET last_active_at = now(),
        first_game_started_at = COALESCE(first_game_started_at, now()),
        total_sessions = CASE WHEN p_event_name = 'game_started' THEN total_sessions + 1 ELSE total_sessions END,
        total_runs = CASE WHEN p_event_name IN ('game_over', 'score_submitted') THEN total_runs + 1 ELSE total_runs END,
        updated_at = now()
    WHERE id = v_player.id;

    -- Insert event record
    INSERT INTO public.gameplay_events (
        player_id,
        event_name,
        session_id,
        score,
        metadata
    ) VALUES (
        v_player.id,
        p_event_name,
        p_session_id,
        p_score,
        p_metadata
    )
    RETURNING id INTO v_event_id;

    RETURN QUERY
    SELECT v_event_id, v_player.id, v_player.player_code;
END;
$$;

REVOKE ALL ON FUNCTION public.track_gameplay_event(BIGINT, TEXT, UUID, INTEGER, JSONB) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.track_gameplay_event(BIGINT, TEXT, UUID, INTEGER, JSONB) TO service_role, postgres;


-- 5. Update submit_score_atomic to automatically track gameplay activity and events
DROP FUNCTION IF EXISTS public.submit_score_atomic(BIGINT, INTEGER);

CREATE OR REPLACE FUNCTION public.submit_score_atomic(
    p_telegram_id BIGINT,
    p_submitted_score INTEGER
)
RETURNS TABLE (
    player_id UUID,
    telegram_id BIGINT,
    player_code VARCHAR(7),
    display_name TEXT,
    best_score INTEGER,
    global_rank BIGINT,
    is_new_high_score BOOLEAN,
    is_new_personal_best BOOLEAN,
    telegram_chat_id BIGINT,
    notification_event_id UUID,
    claim_token UUID
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_player public.leaderboard_players%ROWTYPE;
    v_is_new_pb BOOLEAN := FALSE;
    v_rank BIGINT;
    v_event_id UUID := NULL;
    v_claim_token UUID := NULL;
BEGIN
    IF p_submitted_score < 0 THEN
        RAISE EXCEPTION 'INVALID_SCORE: Score cannot be negative.';
    END IF;

    -- Find and lock player record
    SELECT * INTO v_player
    FROM public.leaderboard_players
    WHERE leaderboard_players.telegram_id = p_telegram_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'PLAYER_NOT_FOUND: Player with Telegram ID % does not exist.', p_telegram_id;
    END IF;

    -- Personal Best Decision Logic:
    -- 1. First-ever score submitted (has_submitted_score is FALSE) -> Always a new personal best (even if 0)!
    -- 2. Subsequent score > best_score -> New personal best!
    -- 3. Subsequent score <= best_score -> NOT a new personal best.
    IF NOT v_player.has_submitted_score THEN
        UPDATE public.leaderboard_players
        SET best_score = p_submitted_score,
            has_submitted_score = TRUE,
            first_game_started_at = COALESCE(first_game_started_at, now()),
            last_active_at = now(),
            total_runs = total_runs + 1,
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;

        v_is_new_pb := TRUE;
    ELSIF p_submitted_score > v_player.best_score THEN
        UPDATE public.leaderboard_players
        SET best_score = p_submitted_score,
            last_active_at = now(),
            total_runs = total_runs + 1,
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;

        v_is_new_pb := TRUE;
    ELSE
        UPDATE public.leaderboard_players
        SET last_active_at = now(),
            total_runs = total_runs + 1,
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;

        v_is_new_pb := FALSE;
    END IF;

    -- Track gameplay analytics events
    INSERT INTO public.gameplay_events (
        player_id,
        event_name,
        score
    ) VALUES (
        v_player.id,
        'score_submitted',
        p_submitted_score
    );

    IF v_is_new_pb THEN
        INSERT INTO public.gameplay_events (
            player_id,
            event_name,
            score
        ) VALUES (
            v_player.id,
            'personal_best_achieved',
            p_submitted_score
        );
    END IF;

    -- Calculate deterministic global rank across ALL registered players
    v_rank := public.get_player_global_rank(v_player.telegram_id);

    -- If new personal best, create notification outbox event (guarantees idempotency via unique constraint)
    IF v_is_new_pb THEN
        v_claim_token := gen_random_uuid();
        BEGIN
            INSERT INTO public.score_notification_events (
                player_id,
                telegram_id,
                score,
                global_rank,
                status,
                claim_token
            ) VALUES (
                v_player.id,
                v_player.telegram_id,
                p_submitted_score,
                v_rank,
                CASE WHEN v_player.telegram_chat_id IS NULL THEN 'skipped' ELSE 'pending' END,
                v_claim_token
            )
            RETURNING id INTO v_event_id;
        EXCEPTION WHEN unique_violation THEN
            -- A notification for this score already exists; suppress duplicate message
            v_event_id := NULL;
            v_claim_token := NULL;
            v_is_new_pb := FALSE;
        END;
    END IF;

    RETURN QUERY
    SELECT 
        v_player.id,
        v_player.telegram_id,
        v_player.player_code,
        v_player.display_name,
        v_player.best_score,
        v_rank,
        v_is_new_pb, -- backward compatibility with is_new_high_score
        v_is_new_pb,
        v_player.telegram_chat_id,
        v_event_id,
        v_claim_token;
END;
$$;

REVOKE ALL ON FUNCTION public.submit_score_atomic(BIGINT, INTEGER) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.submit_score_atomic(BIGINT, INTEGER) TO service_role, postgres;


-- 6. RPC: Admin Overview Metrics Aggregation
CREATE OR REPLACE FUNCTION public.get_admin_overview(
    p_start_date TIMESTAMPTZ DEFAULT NULL,
    p_end_date TIMESTAMPTZ DEFAULT NULL
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_start TIMESTAMPTZ := COALESCE(p_start_date, now() - INTERVAL '30 days');
    v_end TIMESTAMPTZ := COALESCE(p_end_date, now());
    v_prev_start TIMESTAMPTZ := v_start - (v_end - v_start);
    v_now TIMESTAMPTZ := now();
    v_res JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    WITH player_stats AS (
        SELECT 
            count(*)::INT AS total_players,
            count(*) FILTER (WHERE first_game_started_at IS NOT NULL)::INT AS total_started_players,
            count(*) FILTER (WHERE created_at >= (v_now - INTERVAL '24 hours'))::INT AS new_players_today,
            count(*) FILTER (WHERE created_at >= (v_now - INTERVAL '7 days'))::INT AS new_players_7d,
            count(*) FILTER (WHERE created_at >= (v_now - INTERVAL '30 days'))::INT AS new_players_30d,
            count(*) FILTER (WHERE created_at >= v_start AND created_at <= v_end)::INT AS new_players_period,
            count(*) FILTER (WHERE created_at >= v_prev_start AND created_at < v_start)::INT AS new_players_prev_period,
            COALESCE(max(best_score), 0)::INT AS highest_score_ever,
            COALESCE(round(avg(best_score) FILTER (WHERE has_submitted_score = TRUE), 1), 0)::NUMERIC AS avg_best_score
        FROM public.leaderboard_players
    ),
    active_users AS (
        SELECT 
            count(DISTINCT player_id) FILTER (WHERE created_at >= (v_now - INTERVAL '24 hours'))::INT AS dau,
            count(DISTINCT player_id) FILTER (WHERE created_at >= (v_now - INTERVAL '7 days'))::INT AS wau,
            count(DISTINCT player_id) FILTER (WHERE created_at >= (v_now - INTERVAL '30 days'))::INT AS mau,
            count(DISTINCT player_id) FILTER (WHERE created_at >= v_start AND created_at <= v_end)::INT AS active_period,
            count(DISTINCT player_id) FILTER (WHERE created_at >= v_prev_start AND created_at < v_start)::INT AS active_prev_period
        FROM public.gameplay_events
    ),
    gameplay_metrics AS (
        SELECT 
            count(*)::INT AS total_events_period,
            count(*) FILTER (WHERE event_name = 'game_started')::INT AS sessions_period,
            count(*) FILTER (WHERE event_name = 'game_started' AND created_at >= (v_now - INTERVAL '24 hours'))::INT AS sessions_today,
            count(*) FILTER (WHERE event_name = 'score_submitted')::INT AS completed_runs_period,
            count(*) FILTER (WHERE event_name = 'personal_best_achieved')::INT AS new_pbs_period,
            COALESCE(round(avg(score) FILTER (WHERE event_name = 'score_submitted'), 1), 0)::NUMERIC AS avg_score_period
        FROM public.gameplay_events
        WHERE created_at >= v_start AND created_at <= v_end
    ),
    historical_sessions AS (
        SELECT 
            COALESCE(sum(total_sessions), 0)::INT AS total_sessions_all_time,
            COALESCE(sum(total_runs), 0)::INT AS total_runs_all_time
        FROM public.leaderboard_players
    )
    SELECT jsonb_build_object(
        'total_players', ps.total_players,
        'total_started_players', ps.total_started_players,
        'new_players_today', ps.new_players_today,
        'new_players_7d', ps.new_players_7d,
        'new_players_30d', ps.new_players_30d,
        'new_players_period', ps.new_players_period,
        'new_players_prev_period', ps.new_players_prev_period,
        'player_growth_rate_pct', CASE 
            WHEN ps.new_players_prev_period > 0 
            THEN round(((ps.new_players_period - ps.new_players_prev_period)::NUMERIC / ps.new_players_prev_period * 100), 1)
            ELSE 0 
        END,
        'dau', au.dau,
        'wau', au.wau,
        'mau', au.mau,
        'active_period', au.active_period,
        'active_prev_period', au.active_prev_period,
        'active_growth_rate_pct', CASE 
            WHEN au.active_prev_period > 0 
            THEN round(((au.active_period - au.active_prev_period)::NUMERIC / au.active_prev_period * 100), 1)
            ELSE 0 
        END,
        'total_sessions_all_time', GREATEST(hs.total_sessions_all_time, gm.sessions_period),
        'total_runs_all_time', GREATEST(hs.total_runs_all_time, gm.completed_runs_period),
        'sessions_today', gm.sessions_today,
        'sessions_period', gm.sessions_period,
        'avg_sessions_per_player', CASE 
            WHEN ps.total_players > 0 
            THEN round((GREATEST(hs.total_sessions_all_time, gm.sessions_period)::NUMERIC / ps.total_players), 1)
            ELSE 0 
        END,
        'completed_runs_period', gm.completed_runs_period,
        'new_pbs_period', gm.new_pbs_period,
        'avg_score_period', gm.avg_score_period,
        'highest_score_ever', ps.highest_score_ever,
        'avg_best_score', ps.avg_best_score
    ) INTO v_res
    FROM player_stats ps
    CROSS JOIN active_users au
    CROSS JOIN gameplay_metrics gm
    CROSS JOIN historical_sessions hs;

    RETURN v_res;
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_overview(TIMESTAMPTZ, TIMESTAMPTZ) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_overview(TIMESTAMPTZ, TIMESTAMPTZ) TO authenticated, service_role, postgres;


-- 7. RPC: Admin Growth Time-Series Charts Aggregation
CREATE OR REPLACE FUNCTION public.get_admin_growth_charts(
    p_start_date TIMESTAMPTZ DEFAULT NULL,
    p_end_date TIMESTAMPTZ DEFAULT NULL
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_start TIMESTAMPTZ := COALESCE(p_start_date, now() - INTERVAL '30 days');
    v_end TIMESTAMPTZ := COALESCE(p_end_date, now());
    v_res JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    WITH date_series AS (
        SELECT generate_series(
            date_trunc('day', v_start),
            date_trunc('day', v_end),
            INTERVAL '1 day'
        ) AS day
    ),
    daily_registrations AS (
        SELECT 
            date_trunc('day', created_at) AS day,
            count(*)::INT AS new_registrations
        FROM public.leaderboard_players
        WHERE created_at >= v_start AND created_at <= v_end
        GROUP BY 1
    ),
    daily_first_starts AS (
        SELECT 
            date_trunc('day', first_game_started_at) AS day,
            count(*)::INT AS first_starts
        FROM public.leaderboard_players
        WHERE first_game_started_at >= v_start AND first_game_started_at <= v_end
        GROUP BY 1
    ),
    daily_active_users AS (
        SELECT 
            date_trunc('day', created_at) AS day,
            count(DISTINCT player_id)::INT AS dau
        FROM public.gameplay_events
        WHERE created_at >= v_start AND created_at <= v_end
        GROUP BY 1
    ),
    daily_sessions AS (
        SELECT 
            date_trunc('day', created_at) AS day,
            count(*)::INT AS sessions,
            count(*) FILTER (WHERE event_name = 'personal_best_achieved')::INT AS new_pbs,
            COALESCE(round(avg(score) FILTER (WHERE event_name = 'score_submitted'), 1), 0)::NUMERIC AS avg_score
        FROM public.gameplay_events
        WHERE created_at >= v_start AND created_at <= v_end
        GROUP BY 1
    )
    SELECT jsonb_agg(
        jsonb_build_object(
            'date', to_char(ds.day, 'YYYY-MM-DD'),
            'new_registrations', COALESCE(dr.new_registrations, 0),
            'first_starts', COALESCE(dfs.first_starts, 0),
            'dau', COALESCE(dau.dau, 0),
            'sessions', COALESCE(ses.sessions, 0),
            'new_pbs', COALESCE(ses.new_pbs, 0),
            'avg_score', COALESCE(ses.avg_score, 0)
        ) ORDER BY ds.day ASC
    ) INTO v_res
    FROM date_series ds
    LEFT JOIN daily_registrations dr ON dr.day = ds.day
    LEFT JOIN daily_first_starts dfs ON dfs.day = ds.day
    LEFT JOIN daily_active_users dau ON dau.day = ds.day
    LEFT JOIN daily_sessions ses ON ses.day = ds.day;

    RETURN COALESCE(v_res, '[]'::jsonb);
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_growth_charts(TIMESTAMPTZ, TIMESTAMPTZ) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_growth_charts(TIMESTAMPTZ, TIMESTAMPTZ) TO authenticated, service_role, postgres;


-- 8. RPC: Admin Searchable & Filterable Player List
CREATE OR REPLACE FUNCTION public.get_admin_player_list(
    p_search TEXT DEFAULT NULL,
    p_filter TEXT DEFAULT 'all',
    p_limit INT DEFAULT 25,
    p_offset INT DEFAULT 0,
    p_sort_by TEXT DEFAULT 'created_at',
    p_sort_dir TEXT DEFAULT 'desc'
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_total_count INT;
    v_rows JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    -- Base filtered query
    WITH filtered_players AS (
        SELECT 
            lp.id,
            lp.player_code,
            lp.display_name,
            lp.best_score,
            lp.has_submitted_score,
            lp.first_game_started_at,
            lp.last_active_at,
            lp.total_sessions,
            lp.total_runs,
            lp.created_at,
            lp.updated_at,
            -- Calculate status
            CASE 
                WHEN lp.last_active_at >= (now() - INTERVAL '24 hours') THEN 'active_today'
                WHEN lp.last_active_at >= (now() - INTERVAL '7 days') THEN 'active_7d'
                WHEN lp.last_active_at >= (now() - INTERVAL '30 days') THEN 'active_30d'
                WHEN lp.first_game_started_at IS NULL AND NOT lp.has_submitted_score THEN 'never_played'
                ELSE 'inactive'
            END AS activity_status
        FROM public.leaderboard_players lp
        WHERE (
            p_search IS NULL 
            OR p_search = '' 
            OR lp.player_code ILIKE '%' || trim(p_search) || '%'
            OR lp.display_name ILIKE '%' || trim(p_search) || '%'
        )
        AND (
            p_filter = 'all'
            OR (p_filter = 'never_played' AND lp.first_game_started_at IS NULL AND NOT lp.has_submitted_score)
            OR (p_filter = 'active_today' AND lp.last_active_at >= (now() - INTERVAL '24 hours'))
            OR (p_filter = 'active_7d' AND lp.last_active_at >= (now() - INTERVAL '7 days'))
            OR (p_filter = 'active_30d' AND lp.last_active_at >= (now() - INTERVAL '30 days'))
            OR (p_filter = 'inactive' AND (lp.last_active_at < (now() - INTERVAL '30 days') OR (lp.last_active_at IS NULL AND lp.has_submitted_score)))
            OR (p_filter = 'top_scoring' AND lp.best_score > 0)
        )
    ),
    counted AS (
        SELECT count(*)::INT AS total FROM filtered_players
    ),
    sorted_paginated AS (
        SELECT 
            fp.*,
            ROW_NUMBER() OVER (ORDER BY fp.best_score DESC, fp.updated_at ASC, fp.id ASC) AS global_rank
        FROM filtered_players fp
        ORDER BY
            CASE WHEN p_sort_by = 'best_score' AND p_sort_dir = 'asc' THEN fp.best_score END ASC,
            CASE WHEN p_sort_by = 'best_score' AND p_sort_dir = 'desc' THEN fp.best_score END DESC,
            CASE WHEN p_sort_by = 'player_code' AND p_sort_dir = 'asc' THEN fp.player_code END ASC,
            CASE WHEN p_sort_by = 'player_code' AND p_sort_dir = 'desc' THEN fp.player_code END DESC,
            CASE WHEN p_sort_by = 'display_name' AND p_sort_dir = 'asc' THEN fp.display_name END ASC,
            CASE WHEN p_sort_by = 'display_name' AND p_sort_dir = 'desc' THEN fp.display_name END DESC,
            CASE WHEN p_sort_by = 'last_active_at' AND p_sort_dir = 'asc' THEN fp.last_active_at END ASC,
            CASE WHEN p_sort_by = 'last_active_at' AND p_sort_dir = 'desc' THEN fp.last_active_at END DESC,
            CASE WHEN p_sort_by = 'created_at' AND p_sort_dir = 'asc' THEN fp.created_at END ASC,
            CASE WHEN p_sort_by = 'created_at' AND p_sort_dir = 'desc' THEN fp.created_at END DESC,
            fp.created_at DESC
        LIMIT p_limit
        OFFSET p_offset
    )
    SELECT 
        (SELECT total FROM counted),
        jsonb_agg(
            jsonb_build_object(
                'id', sp.id,
                'player_code', sp.player_code,
                'display_name', sp.display_name,
                'best_score', sp.best_score,
                'global_rank', sp.global_rank,
                'has_submitted_score', sp.has_submitted_score,
                'first_game_started_at', sp.first_game_started_at,
                'last_active_at', sp.last_active_at,
                'total_sessions', sp.total_sessions,
                'total_runs', sp.total_runs,
                'activity_status', sp.activity_status,
                'created_at', sp.created_at
            )
        )
    INTO v_total_count, v_rows
    FROM sorted_paginated sp;

    RETURN jsonb_build_object(
        'total_count', COALESCE(v_total_count, 0),
        'limit', p_limit,
        'offset', p_offset,
        'players', COALESCE(v_rows, '[]'::jsonb)
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_player_list(TEXT, TEXT, INT, INT, TEXT, TEXT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_player_list(TEXT, TEXT, INT, INT, TEXT, TEXT) TO authenticated, service_role, postgres;


-- 9. RPC: Admin Detailed Player Profile
CREATE OR REPLACE FUNCTION public.get_admin_player_detail(
    p_player_code VARCHAR(7)
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_player public.leaderboard_players%ROWTYPE;
    v_rank BIGINT;
    v_events JSONB;
    v_score_history JSONB;
    v_notifications JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    SELECT * INTO v_player
    FROM public.leaderboard_players
    WHERE player_code = p_player_code;

    IF NOT FOUND THEN
        RETURN jsonb_build_object('error', 'PLAYER_NOT_FOUND');
    END IF;

    -- Calculate rank
    v_rank := public.get_player_global_rank(v_player.telegram_id);

    -- Recent gameplay events
    SELECT jsonb_agg(
        jsonb_build_object(
            'id', ge.id,
            'event_name', ge.event_name,
            'score', ge.score,
            'created_at', ge.created_at,
            'metadata', ge.metadata
        ) ORDER BY ge.created_at DESC
    ) INTO v_events
    FROM (
        SELECT * FROM public.gameplay_events
        WHERE player_id = v_player.id
        ORDER BY created_at DESC
        LIMIT 50
    ) ge;

    -- Score history
    SELECT jsonb_agg(
        jsonb_build_object(
            'score', ge.score,
            'created_at', ge.created_at
        ) ORDER BY ge.created_at ASC
    ) INTO v_score_history
    FROM public.gameplay_events ge
    WHERE ge.player_id = v_player.id
      AND ge.event_name IN ('score_submitted', 'personal_best_achieved')
      AND ge.score IS NOT NULL;

    -- Score notification history (without sensitive IDs)
    SELECT jsonb_agg(
        jsonb_build_object(
            'id', sne.id,
            'score', sne.score,
            'global_rank', sne.global_rank,
            'status', sne.status,
            'created_at', sne.created_at,
            'sent_at', sne.sent_at,
            'error_message', sne.error_message
        ) ORDER BY sne.created_at DESC
    ) INTO v_notifications
    FROM public.score_notification_events sne
    WHERE sne.player_id = v_player.id;

    RETURN jsonb_build_object(
        'id', v_player.id,
        'player_code', v_player.player_code,
        'display_name', v_player.display_name,
        'best_score', v_player.best_score,
        'global_rank', v_rank,
        'has_submitted_score', v_player.has_submitted_score,
        'has_telegram_chat', (v_player.telegram_chat_id IS NOT NULL),
        'first_game_started_at', v_player.first_game_started_at,
        'last_active_at', v_player.last_active_at,
        'total_sessions', v_player.total_sessions,
        'total_runs', v_player.total_runs,
        'created_at', v_player.created_at,
        'events', COALESCE(v_events, '[]'::jsonb),
        'score_history', COALESCE(v_score_history, '[]'::jsonb),
        'notifications', COALESCE(v_notifications, '[]'::jsonb)
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_player_detail(VARCHAR(7)) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_player_detail(VARCHAR(7)) TO authenticated, service_role, postgres;


-- 10. RPC: Admin Retention & Cohort Analytics
CREATE OR REPLACE FUNCTION public.get_admin_retention_analytics()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_total_players INT;
    v_d1_retention NUMERIC;
    v_d7_retention NUMERIC;
    v_d30_retention NUMERIC;
    v_cohorts JSONB;
    v_inactivity JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    SELECT count(*) INTO v_total_players FROM public.leaderboard_players;

    -- Day 1, 7, 30 Retention Rates based on gameplay events
    -- A player is retained on Day N if they played a game >= N days after their first game start
    WITH player_first_start AS (
        SELECT 
            id AS player_id,
            COALESCE(first_game_started_at, created_at) AS first_start
        FROM public.leaderboard_players
        WHERE first_game_started_at IS NOT NULL OR has_submitted_score = TRUE
    ),
    retention_rates AS (
        SELECT 
            count(*)::INT AS cohort_base,
            count(*) FILTER (
                WHERE EXISTS (
                    SELECT 1 FROM public.gameplay_events ge 
                    WHERE ge.player_id = pfs.player_id 
                      AND ge.created_at >= (pfs.first_start + INTERVAL '24 hours')
                      AND ge.created_at < (pfs.first_start + INTERVAL '48 hours')
                )
            )::INT AS retained_d1,
            count(*) FILTER (
                WHERE EXISTS (
                    SELECT 1 FROM public.gameplay_events ge 
                    WHERE ge.player_id = pfs.player_id 
                      AND ge.created_at >= (pfs.first_start + INTERVAL '7 days')
                      AND ge.created_at < (pfs.first_start + INTERVAL '8 days')
                )
            )::INT AS retained_d7,
            count(*) FILTER (
                WHERE EXISTS (
                    SELECT 1 FROM public.gameplay_events ge 
                    WHERE ge.player_id = pfs.player_id 
                      AND ge.created_at >= (pfs.first_start + INTERVAL '30 days')
                      AND ge.created_at < (pfs.first_start + INTERVAL '31 days')
                )
            )::INT AS retained_d30
        FROM player_first_start pfs
    )
    SELECT 
        CASE WHEN cohort_base > 0 THEN round((retained_d1::NUMERIC / cohort_base * 100), 1) ELSE 0 END,
        CASE WHEN cohort_base > 0 THEN round((retained_d7::NUMERIC / cohort_base * 100), 1) ELSE 0 END,
        CASE WHEN cohort_base > 0 THEN round((retained_d30::NUMERIC / cohort_base * 100), 1) ELSE 0 END
    INTO v_d1_retention, v_d7_retention, v_d30_retention
    FROM retention_rates;

    -- Inactivity distribution
    WITH inactivity_groups AS (
        SELECT 
            count(*) FILTER (WHERE last_active_at >= now() - INTERVAL '3 days')::INT AS active_last_3d,
            count(*) FILTER (WHERE last_active_at >= now() - INTERVAL '7 days' AND last_active_at < now() - INTERVAL '3 days')::INT AS inactive_3_7d,
            count(*) FILTER (WHERE last_active_at >= now() - INTERVAL '14 days' AND last_active_at < now() - INTERVAL '7 days')::INT AS inactive_7_14d,
            count(*) FILTER (WHERE last_active_at >= now() - INTERVAL '30 days' AND last_active_at < now() - INTERVAL '14 days')::INT AS inactive_14_30d,
            count(*) FILTER (WHERE last_active_at < now() - INTERVAL '30 days' OR (last_active_at IS NULL AND has_submitted_score))::INT AS inactive_gt_30d,
            count(*) FILTER (WHERE first_game_started_at IS NULL AND NOT has_submitted_score)::INT AS never_played
        FROM public.leaderboard_players
    )
    SELECT jsonb_build_object(
        'active_last_3d', active_last_3d,
        'inactive_3_7d', inactive_3_7d,
        'inactive_7_14d', inactive_7_14d,
        'inactive_14_30d', inactive_14_30d,
        'inactive_gt_30d', inactive_gt_30d,
        'never_played', never_played
    ) INTO v_inactivity
    FROM inactivity_groups;

    -- Weekly cohorts (last 8 weeks)
    WITH weekly_cohorts AS (
        SELECT 
            date_trunc('week', COALESCE(first_game_started_at, created_at)) AS cohort_week,
            count(DISTINCT id)::INT AS cohort_size,
            count(DISTINCT id) FILTER (
                WHERE last_active_at >= date_trunc('week', COALESCE(first_game_started_at, created_at)) + INTERVAL '1 week'
            )::INT AS week_1,
            count(DISTINCT id) FILTER (
                WHERE last_active_at >= date_trunc('week', COALESCE(first_game_started_at, created_at)) + INTERVAL '2 weeks'
            )::INT AS week_2,
            count(DISTINCT id) FILTER (
                WHERE last_active_at >= date_trunc('week', COALESCE(first_game_started_at, created_at)) + INTERVAL '3 weeks'
            )::INT AS week_3,
            count(DISTINCT id) FILTER (
                WHERE last_active_at >= date_trunc('week', COALESCE(first_game_started_at, created_at)) + INTERVAL '4 weeks'
            )::INT AS week_4
        FROM public.leaderboard_players
        WHERE created_at >= (now() - INTERVAL '8 weeks')
        GROUP BY 1
        ORDER BY 1 DESC
    )
    SELECT jsonb_agg(
        jsonb_build_object(
            'cohort_week', to_char(cohort_week, 'YYYY-MM-DD'),
            'cohort_size', cohort_size,
            'week_1_pct', CASE WHEN cohort_size > 0 THEN round((week_1::NUMERIC / cohort_size * 100), 1) ELSE 0 END,
            'week_2_pct', CASE WHEN cohort_size > 0 THEN round((week_2::NUMERIC / cohort_size * 100), 1) ELSE 0 END,
            'week_3_pct', CASE WHEN cohort_size > 0 THEN round((week_3::NUMERIC / cohort_size * 100), 1) ELSE 0 END,
            'week_4_pct', CASE WHEN cohort_size > 0 THEN round((week_4::NUMERIC / cohort_size * 100), 1) ELSE 0 END
        )
    ) INTO v_cohorts
    FROM weekly_cohorts;

    RETURN jsonb_build_object(
        'day_1_retention_pct', COALESCE(v_d1_retention, 0),
        'day_7_retention_pct', COALESCE(v_d7_retention, 0),
        'day_30_retention_pct', COALESCE(v_d30_retention, 0),
        'inactivity_distribution', COALESCE(v_inactivity, '{}'::jsonb),
        'weekly_cohorts', COALESCE(v_cohorts, '[]'::jsonb),
        'calculation_method', 'Retention is calculated exclusively from real gameplay activity events (runs and scores), comparing repeat activity timestamps against each player''s first gameplay start.'
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_retention_analytics() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_retention_analytics() TO authenticated, service_role, postgres;


-- 11. RPC: Admin Leaderboard & Score Distribution Analytics
CREATE OR REPLACE FUNCTION public.get_admin_leaderboard_analytics()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_top100 JSONB;
    v_distribution JSONB;
    v_stats JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    -- Top 100
    SELECT jsonb_agg(
        jsonb_build_object(
            'rank', rank,
            'player_code', player_code,
            'display_name', display_name,
            'best_score', best_score,
            'rank_display', rank_display
        )
    ) INTO v_top100
    FROM public.get_top_leaderboard(100);

    -- Score bracket distribution
    WITH brackets AS (
        SELECT 
            count(*) FILTER (WHERE best_score = 0)::INT AS bracket_0,
            count(*) FILTER (WHERE best_score BETWEEN 1 AND 10)::INT AS bracket_1_10,
            count(*) FILTER (WHERE best_score BETWEEN 11 AND 25)::INT AS bracket_11_25,
            count(*) FILTER (WHERE best_score BETWEEN 26 AND 50)::INT AS bracket_26_50,
            count(*) FILTER (WHERE best_score BETWEEN 51 AND 100)::INT AS bracket_51_100,
            count(*) FILTER (WHERE best_score BETWEEN 101 AND 250)::INT AS bracket_101_250,
            count(*) FILTER (WHERE best_score > 250)::INT AS bracket_250_plus
        FROM public.leaderboard_players
    )
    SELECT jsonb_build_object(
        '0', bracket_0,
        '1-10', bracket_1_10,
        '11-25', bracket_11_25,
        '26-50', bracket_26_50,
        '51-100', bracket_51_100,
        '101-250', bracket_101_250,
        '250+', bracket_250_plus
    ) INTO v_distribution
    FROM brackets;

    -- Overall stats
    SELECT jsonb_build_object(
        'total_registered', count(*)::INT,
        'total_scored', count(*) FILTER (WHERE best_score > 0)::INT,
        'pct_scored', CASE WHEN count(*) > 0 THEN round((count(*) FILTER (WHERE best_score > 0)::NUMERIC / count(*) * 100), 1) ELSE 0 END,
        'highest_score', COALESCE(max(best_score), 0)::INT,
        'avg_score', COALESCE(round(avg(best_score) FILTER (WHERE best_score > 0), 1), 0)::NUMERIC
    ) INTO v_stats
    FROM public.leaderboard_players;

    RETURN jsonb_build_object(
        'stats', COALESCE(v_stats, '{}'::jsonb),
        'score_distribution', COALESCE(v_distribution, '{}'::jsonb),
        'top_100', COALESCE(v_top100, '[]'::jsonb)
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_leaderboard_analytics() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_leaderboard_analytics() TO authenticated, service_role, postgres;


-- 12. RPC: Admin Telegram Notification Monitoring
CREATE OR REPLACE FUNCTION public.get_admin_notification_monitoring()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_stats JSONB;
    v_recent_events JSONB;
    v_recent_errors JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    WITH counts AS (
        SELECT 
            count(*)::INT AS total_events,
            count(*) FILTER (WHERE status = 'sent')::INT AS sent_count,
            count(*) FILTER (WHERE status IN ('pending', 'processing'))::INT AS pending_count,
            count(*) FILTER (WHERE status = 'failed')::INT AS failed_count,
            count(*) FILTER (WHERE status = 'skipped')::INT AS skipped_count
        FROM public.score_notification_events
    )
    SELECT jsonb_build_object(
        'total_events', total_events,
        'sent_count', sent_count,
        'pending_count', pending_count,
        'failed_count', failed_count,
        'skipped_count', skipped_count,
        'success_rate_pct', CASE 
            WHEN (sent_count + failed_count) > 0 
            THEN round((sent_count::NUMERIC / (sent_count + failed_count) * 100), 1)
            ELSE 100.0 
        END
    ) INTO v_stats
    FROM counts;

    -- Recent 20 notification events (safely joined with player_code, no telegram_id exposed)
    SELECT jsonb_agg(
        jsonb_build_object(
            'id', sne.id,
            'player_code', lp.player_code,
            'display_name', lp.display_name,
            'score', sne.score,
            'global_rank', sne.global_rank,
            'status', sne.status,
            'retry_count', sne.retry_count,
            'error_message', sne.error_message,
            'created_at', sne.created_at,
            'updated_at', sne.updated_at,
            'sent_at', sne.sent_at
        ) ORDER BY sne.created_at DESC
    ) INTO v_recent_events
    FROM (
        SELECT * FROM public.score_notification_events
        ORDER BY created_at DESC
        LIMIT 20
    ) sne
    JOIN public.leaderboard_players lp ON lp.id = sne.player_id;

    -- Recent errors
    SELECT jsonb_agg(
        jsonb_build_object(
            'id', sne.id,
            'player_code', lp.player_code,
            'error_message', sne.error_message,
            'retry_count', sne.retry_count,
            'created_at', sne.created_at
        ) ORDER BY sne.updated_at DESC
    ) INTO v_recent_errors
    FROM public.score_notification_events sne
    JOIN public.leaderboard_players lp ON lp.id = sne.player_id
    WHERE sne.status = 'failed' OR sne.error_message IS NOT NULL
    LIMIT 10;

    RETURN jsonb_build_object(
        'stats', COALESCE(v_stats, '{}'::jsonb),
        'recent_events', COALESCE(v_recent_events, '[]'::jsonb),
        'recent_errors', COALESCE(v_recent_errors, '[]'::jsonb)
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_notification_monitoring() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_notification_monitoring() TO authenticated, service_role, postgres;


-- 13. RPC: Admin System Health Overview
CREATE OR REPLACE FUNCTION public.get_admin_system_health()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_last_event TIMESTAMPTZ;
    v_last_score TIMESTAMPTZ;
    v_pending_notifications INT;
    v_db_time TIMESTAMPTZ := now();
    v_total_players INT;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    SELECT max(created_at) INTO v_last_event FROM public.gameplay_events;
    SELECT max(updated_at) FILTER (WHERE has_submitted_score = TRUE) INTO v_last_score FROM public.leaderboard_players;
    SELECT count(*)::INT INTO v_pending_notifications FROM public.score_notification_events WHERE status IN ('pending', 'processing');
    SELECT count(*)::INT INTO v_total_players FROM public.leaderboard_players;

    RETURN jsonb_build_object(
        'status', 'healthy',
        'database_time', v_db_time,
        'database_version', 'PostgreSQL 17.11 (Supabase Managed)',
        'total_registered_players', v_total_players,
        'notification_queue_backlog', v_pending_notifications,
        'last_gameplay_event_at', v_last_event,
        'last_score_submitted_at', v_last_score,
        'edge_functions', jsonb_build_object(
            'submit-score', 'ACTIVE (v3)',
            'get-leaderboard', 'ACTIVE (v2)',
            'get-or-create-player', 'ACTIVE (v2)',
            'telegram-webhook', 'ACTIVE (v1)',
            'track-gameplay-event', 'ACTIVE (v1)'
        )
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_system_health() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_system_health() TO authenticated, service_role, postgres;


-- 14. RPC: Admin Activity Stream Feed
CREATE OR REPLACE FUNCTION public.get_admin_activity_feed(
    p_event_name TEXT DEFAULT 'all',
    p_limit INT DEFAULT 50,
    p_offset INT DEFAULT 0
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_total_count INT;
    v_rows JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    WITH filtered AS (
        SELECT 
            ge.id,
            ge.event_name,
            ge.score,
            ge.metadata,
            ge.created_at,
            lp.player_code,
            lp.display_name
        FROM public.gameplay_events ge
        JOIN public.leaderboard_players lp ON lp.id = ge.player_id
        WHERE (p_event_name = 'all' OR ge.event_name = p_event_name)
    ),
    counted AS (
        SELECT count(*)::INT AS total FROM filtered
    ),
    paginated AS (
        SELECT * FROM filtered
        ORDER BY created_at DESC
        LIMIT p_limit OFFSET p_offset
    )
    SELECT 
        (SELECT total FROM counted),
        jsonb_agg(
            jsonb_build_object(
                'id', p.id,
                'event_name', p.event_name,
                'score', p.score,
                'metadata', p.metadata,
                'created_at', p.created_at,
                'player_code', p.player_code,
                'display_name', p.display_name
            )
        )
    INTO v_total_count, v_rows
    FROM paginated p;

    RETURN jsonb_build_object(
        'total_count', COALESCE(v_total_count, 0),
        'limit', p_limit,
        'offset', p_offset,
        'events', COALESCE(v_rows, '[]'::jsonb)
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_activity_feed(TEXT, INT, INT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_activity_feed(TEXT, INT, INT) TO authenticated, service_role, postgres;


-- 15. RPC: Admin Gameplay Analytics
CREATE OR REPLACE FUNCTION public.get_admin_gameplay_analytics()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_total_sessions INT;
    v_total_runs INT;
    v_completion_rate NUMERIC;
    v_score_brackets JSONB;
    v_score_stats JSONB;
    v_session_stats JSONB;
BEGIN
    IF NOT (public.is_admin() OR auth.role() = 'service_role' OR current_user = 'postgres') THEN
        RAISE EXCEPTION 'UNAUTHORIZED: Admin privilege required.';
    END IF;

    -- Aggregate session and run totals
    SELECT 
        GREATEST(COALESCE(sum(total_sessions), 0), (SELECT count(*) FROM public.gameplay_events WHERE event_name = 'game_started'))::INT,
        GREATEST(COALESCE(sum(total_runs), 0), (SELECT count(*) FROM public.gameplay_events WHERE event_name IN ('score_submitted', 'game_over')))::INT
    INTO v_total_sessions, v_total_runs
    FROM public.leaderboard_players;

    v_completion_rate := CASE 
        WHEN v_total_sessions > 0 THEN round((v_total_runs::NUMERIC / v_total_sessions * 100), 1)
        ELSE 0 
    END;

    -- Score distribution across all submitted scores
    WITH submitted_scores AS (
        SELECT score FROM public.gameplay_events WHERE event_name = 'score_submitted' AND score IS NOT NULL
        UNION ALL
        SELECT best_score AS score FROM public.leaderboard_players WHERE has_submitted_score = TRUE AND best_score > 0
    ),
    score_counts AS (
        SELECT 
            count(*) FILTER (WHERE score BETWEEN 0 AND 10)::INT AS b_0_10,
            count(*) FILTER (WHERE score BETWEEN 11 AND 25)::INT AS b_11_25,
            count(*) FILTER (WHERE score BETWEEN 26 AND 50)::INT AS b_26_50,
            count(*) FILTER (WHERE score BETWEEN 51 AND 100)::INT AS b_51_100,
            count(*) FILTER (WHERE score BETWEEN 101 AND 200)::INT AS b_101_200,
            count(*) FILTER (WHERE score > 200)::INT AS b_200_plus,
            COALESCE(round(avg(score), 1), 0)::NUMERIC AS avg_score,
            COALESCE(max(score), 0)::INT AS max_score,
            COALESCE(min(score), 0)::INT AS min_score,
            count(*)::INT AS total_scores_recorded
        FROM submitted_scores
    )
    SELECT 
        jsonb_build_object(
            '0-10', b_0_10,
            '11-25', b_11_25,
            '26-50', b_26_50,
            '51-100', b_51_100,
            '101-200', b_101_200,
            '200+', b_200_plus
        ),
        jsonb_build_object(
            'avg_score', avg_score,
            'max_score', max_score,
            'min_score', min_score,
            'total_scores_recorded', total_scores_recorded
        )
    INTO v_score_brackets, v_score_stats
    FROM score_counts;

    -- Session engagement stats
    SELECT jsonb_build_object(
        'total_sessions', v_total_sessions,
        'total_runs', v_total_runs,
        'completion_rate_pct', v_completion_rate,
        'avg_runs_per_player', CASE 
            WHEN count(*) > 0 THEN round((v_total_runs::NUMERIC / count(*)), 1) 
            ELSE 0 
        END,
        'avg_sessions_per_player', CASE 
            WHEN count(*) > 0 THEN round((v_total_sessions::NUMERIC / count(*)), 1) 
            ELSE 0 
        END
    ) INTO v_session_stats
    FROM public.leaderboard_players;

    RETURN jsonb_build_object(
        'session_stats', v_session_stats,
        'score_stats', v_score_stats,
        'score_distribution', v_score_brackets
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_admin_gameplay_analytics() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_admin_gameplay_analytics() TO authenticated, service_role, postgres;
