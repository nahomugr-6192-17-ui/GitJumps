-- ==============================================================================
-- GitJumps Global Leaderboard — Hardened Database Migration
-- Table: leaderboard_players
-- ==============================================================================

-- 1. Create a dedicated sequence for unique, concurrency-safe player code allocation (0000 to 9999)
CREATE SEQUENCE IF NOT EXISTS gitjumps_player_code_seq
    MINVALUE 0
    MAXVALUE 9999
    START WITH 0
    INCREMENT BY 1
    NO CYCLE;

-- 2. Create the leaderboard_players table
CREATE TABLE IF NOT EXISTS public.leaderboard_players (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    telegram_id BIGINT NOT NULL,
    player_code VARCHAR(7) NOT NULL,
    display_name TEXT NOT NULL,
    best_score INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_leaderboard_players_telegram_id UNIQUE (telegram_id),
    CONSTRAINT uq_leaderboard_players_player_code UNIQUE (player_code),
    CONSTRAINT chk_leaderboard_players_player_code_format CHECK (player_code ~ '^GJ-[0-9]{4}$'),
    CONSTRAINT chk_leaderboard_players_best_score_non_negative CHECK (best_score >= 0)
);

-- 3. Indexes for deterministic sorting, fast lookup, and leaderboards
CREATE INDEX IF NOT EXISTS idx_leaderboard_players_telegram_id 
    ON public.leaderboard_players (telegram_id);

CREATE INDEX IF NOT EXISTS idx_leaderboard_players_ranking 
    ON public.leaderboard_players (best_score DESC, updated_at ASC, id ASC);

-- 4. Enable Row Level Security (RLS)
ALTER TABLE public.leaderboard_players ENABLE ROW LEVEL SECURITY;

-- 5. Hardened RLS Policies & Permissions:
-- Completely block direct public/anonymous table queries to prevent leakage of telegram_id or private IDs.
REVOKE ALL ON TABLE public.leaderboard_players FROM anon, authenticated;

DROP POLICY IF EXISTS "Deny all public direct access" ON public.leaderboard_players;
CREATE POLICY "Deny all public direct access"
    ON public.leaderboard_players
    FOR ALL
    TO anon, authenticated
    USING (false)
    WITH CHECK (false);

-- Service role (used by Edge Functions) retains full access
GRANT ALL ON TABLE public.leaderboard_players TO service_role;
GRANT USAGE, SELECT ON SEQUENCE gitjumps_player_code_seq TO service_role;


-- ==============================================================================
-- Stored Procedures / RPC Functions (SECURITY DEFINER with fixed search_path)
-- ==============================================================================

-- Function 1: Allocate next available player code (GJ-0000 to GJ-9999)
CREATE OR REPLACE FUNCTION public.allocate_next_player_code()
RETURNS VARCHAR(7)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_next_val BIGINT;
    v_code VARCHAR(7);
BEGIN
    -- Check total count before attempting to prevent unhandled sequence overflow
    IF (SELECT count(*) FROM public.leaderboard_players) >= 10000 THEN
        RAISE EXCEPTION 'PLAYER_CODE_EXHAUSTED: All 10,000 GitJumps player codes (GJ-0000 to GJ-9999) have been allocated.'
            USING ERRCODE = '54000';
    END IF;

    BEGIN
        v_next_val := nextval('gitjumps_player_code_seq');
    EXCEPTION WHEN sequence_generator_limit_exceeded THEN
        RAISE EXCEPTION 'PLAYER_CODE_EXHAUSTED: All 10,000 GitJumps player codes (GJ-0000 to GJ-9999) have been allocated.'
            USING ERRCODE = '54000';
    END;

    v_code := 'GJ-' || lpad(v_next_val::TEXT, 4, '0');
    RETURN v_code;
END;
$$;


-- Function 2: Calculate deterministic global rank for a player
CREATE OR REPLACE FUNCTION public.get_player_global_rank(p_telegram_id BIGINT)
RETURNS BIGINT
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
STABLE
AS $$
DECLARE
    v_score INTEGER;
    v_updated_at TIMESTAMPTZ;
    v_id UUID;
    v_rank BIGINT;
BEGIN
    SELECT best_score, updated_at, id
    INTO v_score, v_updated_at, v_id
    FROM public.leaderboard_players
    WHERE telegram_id = p_telegram_id;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    -- Strict deterministic tiebreaker: higher best_score, then earlier updated_at, then lower UUID id
    SELECT count(*) + 1
    INTO v_rank
    FROM public.leaderboard_players
    WHERE (best_score > v_score)
       OR (best_score = v_score AND updated_at < v_updated_at)
       OR (best_score = v_score AND updated_at = v_updated_at AND id < v_id);

    RETURN v_rank;
END;
$$;


-- Function 3: Get or create player profile atomically with concurrency conflict handling
CREATE OR REPLACE FUNCTION public.get_or_create_player(
    p_telegram_id BIGINT,
    p_display_name TEXT
)
RETURNS TABLE (
    player_id UUID,
    telegram_id BIGINT,
    player_code VARCHAR(7),
    display_name TEXT,
    best_score INTEGER,
    global_rank BIGINT,
    is_new_player BOOLEAN
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_player public.leaderboard_players%ROWTYPE;
    v_is_new BOOLEAN := FALSE;
    v_new_code VARCHAR(7);
    v_clean_name TEXT;
    v_rank BIGINT;
BEGIN
    -- Sanitize display name
    v_clean_name := trim(coalesce(p_display_name, 'Player'));
    IF length(v_clean_name) = 0 THEN
        v_clean_name := 'Player';
    END IF;
    IF length(v_clean_name) > 64 THEN
        v_clean_name := substring(v_clean_name FROM 1 FOR 64);
    END IF;

    -- Check if player exists with row locking
    SELECT * INTO v_player
    FROM public.leaderboard_players
    WHERE leaderboard_players.telegram_id = p_telegram_id
    FOR UPDATE;

    IF FOUND THEN
        -- Refresh display name if changed
        IF v_player.display_name IS DISTINCT FROM v_clean_name THEN
            UPDATE public.leaderboard_players
            SET display_name = v_clean_name,
                updated_at = now()
            WHERE id = v_player.id
            RETURNING * INTO v_player;
        END IF;
    ELSE
        -- Concurrent-safe insert with fallback
        BEGIN
            v_new_code := public.allocate_next_player_code();

            INSERT INTO public.leaderboard_players (
                telegram_id,
                player_code,
                display_name,
                best_score,
                created_at,
                updated_at
            ) VALUES (
                p_telegram_id,
                v_new_code,
                v_clean_name,
                0,
                now(),
                now()
            )
            RETURNING * INTO v_player;

            v_is_new := TRUE;
        EXCEPTION WHEN unique_violation THEN
            -- Another concurrent transaction inserted this telegram_id simultaneously
            SELECT * INTO v_player
            FROM public.leaderboard_players
            WHERE leaderboard_players.telegram_id = p_telegram_id;
            v_is_new := FALSE;
        END;
    END IF;

    -- Calculate rank
    v_rank := public.get_player_global_rank(v_player.telegram_id);

    RETURN QUERY
    SELECT 
        v_player.id,
        v_player.telegram_id,
        v_player.player_code,
        v_player.display_name,
        v_player.best_score,
        v_rank,
        v_is_new;
END;
$$;


-- Function 4: Submit score atomically with anti-overwrite guarantees
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
    is_new_high_score BOOLEAN
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_player public.leaderboard_players%ROWTYPE;
    v_is_new_high BOOLEAN := FALSE;
    v_rank BIGINT;
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

    -- Only update if submitted score strictly exceeds existing personal best
    IF p_submitted_score > v_player.best_score THEN
        UPDATE public.leaderboard_players
        SET best_score = p_submitted_score,
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;

        v_is_new_high := TRUE;
    END IF;

    -- Calculate updated rank
    v_rank := public.get_player_global_rank(v_player.telegram_id);

    RETURN QUERY
    SELECT 
        v_player.id,
        v_player.telegram_id,
        v_player.player_code,
        v_player.display_name,
        v_player.best_score,
        v_rank,
        v_is_new_high;
END;
$$;


-- Function 5: Retrieve Top N leaderboard (capped at 100 max) with deterministic ranks and medals
CREATE OR REPLACE FUNCTION public.get_top_leaderboard(
    p_limit INTEGER DEFAULT 100
)
RETURNS TABLE (
    rank BIGINT,
    display_name TEXT,
    best_score INTEGER,
    player_code VARCHAR(7),
    rank_display TEXT
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
STABLE
AS $$
DECLARE
    v_clamped_limit INTEGER;
BEGIN
    -- Strictly clamp limit between 1 and 100 to enforce Top 100 ceiling
    v_clamped_limit := LEAST(GREATEST(COALESCE(p_limit, 100), 1), 100);

    RETURN QUERY
    WITH ranked_players AS (
        SELECT 
            ROW_NUMBER() OVER (
                ORDER BY lp.best_score DESC, lp.updated_at ASC, lp.id ASC
            ) AS calc_rank,
            lp.display_name,
            lp.best_score,
            lp.player_code
        FROM public.leaderboard_players lp
        ORDER BY lp.best_score DESC, lp.updated_at ASC, lp.id ASC
        LIMIT v_clamped_limit
    )
    SELECT 
        rp.calc_rank,
        rp.display_name,
        rp.best_score,
        rp.player_code,
        CASE 
            WHEN rp.calc_rank = 1 THEN '🥇'
            WHEN rp.calc_rank = 2 THEN '🥈'
            WHEN rp.calc_rank = 3 THEN '🥉'
            ELSE rp.calc_rank::TEXT
        END AS rank_display
    FROM ranked_players rp
    ORDER BY rp.calc_rank ASC;
END;
$$;


-- Function Access Control:
-- Internal procedures are only executable by service_role (invoked via Edge Functions)
REVOKE ALL ON FUNCTION public.allocate_next_player_code() FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.allocate_next_player_code() TO service_role;

REVOKE ALL ON FUNCTION public.get_player_global_rank(BIGINT) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.get_player_global_rank(BIGINT) TO service_role;

REVOKE ALL ON FUNCTION public.get_or_create_player(BIGINT, TEXT) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.get_or_create_player(BIGINT, TEXT) TO service_role;

REVOKE ALL ON FUNCTION public.submit_score_atomic(BIGINT, INTEGER) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.submit_score_atomic(BIGINT, INTEGER) TO service_role;

-- Public can ONLY view the sanitized top leaderboard (no telegram_id, no UUIDs)
GRANT EXECUTE ON FUNCTION public.get_top_leaderboard(INTEGER) TO anon, authenticated, service_role;
