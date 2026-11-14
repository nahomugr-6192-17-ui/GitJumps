-- ==============================================================================
-- GitJumps Telegram New Personal Best Notifications — Migration 002
-- ==============================================================================

-- 1. Add telegram_chat_id and has_submitted_score to leaderboard_players
ALTER TABLE public.leaderboard_players 
    ADD COLUMN IF NOT EXISTS telegram_chat_id BIGINT,
    ADD COLUMN IF NOT EXISTS has_submitted_score BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_leaderboard_players_telegram_chat_id 
    ON public.leaderboard_players (telegram_chat_id);

-- Backfill existing players who already have a recorded score > 0
UPDATE public.leaderboard_players
SET has_submitted_score = TRUE
WHERE best_score > 0 AND NOT has_submitted_score;


-- 2. Outbox / Audit Table for Score Notification Events (Prevents duplicate messages & enables recovery)
CREATE TABLE IF NOT EXISTS public.score_notification_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id UUID NOT NULL REFERENCES public.leaderboard_players(id) ON DELETE CASCADE,
    telegram_id BIGINT NOT NULL,
    score INTEGER NOT NULL,
    global_rank BIGINT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    retry_count INTEGER NOT NULL DEFAULT 0,
    claim_token UUID DEFAULT gen_random_uuid(),
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    sent_at TIMESTAMPTZ,
    CONSTRAINT chk_notification_status CHECK (status IN ('pending', 'processing', 'sent', 'failed', 'skipped')),
    CONSTRAINT uq_notification_player_score UNIQUE (player_id, score)
);

ALTER TABLE public.score_notification_events 
    ADD COLUMN IF NOT EXISTS claim_token UUID DEFAULT gen_random_uuid();

CREATE INDEX IF NOT EXISTS idx_score_notification_events_player_id 
    ON public.score_notification_events (player_id);

CREATE INDEX IF NOT EXISTS idx_score_notification_events_status 
    ON public.score_notification_events (status);

CREATE INDEX IF NOT EXISTS idx_score_notification_events_recovery 
    ON public.score_notification_events (status, updated_at)
    WHERE status IN ('pending', 'processing', 'failed');

-- 3. Row Level Security for score_notification_events
ALTER TABLE public.score_notification_events ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.score_notification_events FROM anon, authenticated;

DROP POLICY IF EXISTS "Deny all public direct access to notifications" ON public.score_notification_events;
CREATE POLICY "Deny all public direct access to notifications"
    ON public.score_notification_events
    FOR ALL
    TO anon, authenticated
    USING (false)
    WITH CHECK (false);

GRANT ALL ON TABLE public.score_notification_events TO service_role;


-- 4. RPC: Register private chat ID via verified Telegram /start webhook
CREATE OR REPLACE FUNCTION public.register_telegram_chat(
    p_telegram_id BIGINT,
    p_chat_id BIGINT,
    p_display_name TEXT DEFAULT NULL
)
RETURNS TABLE (
    player_id UUID,
    player_code VARCHAR(7),
    display_name TEXT,
    telegram_chat_id BIGINT
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_player public.leaderboard_players%ROWTYPE;
    v_code VARCHAR(7);
    v_name TEXT;
BEGIN
    -- Check if player already exists
    SELECT * INTO v_player
    FROM public.leaderboard_players
    WHERE leaderboard_players.telegram_id = p_telegram_id
    FOR UPDATE;

    IF FOUND THEN
        UPDATE public.leaderboard_players
        SET telegram_chat_id = p_chat_id,
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;
    ELSE
        -- Auto-allocate code if player starts bot before opening game
        v_code := public.allocate_next_player_code();
        v_name := COALESCE(NULLIF(trim(p_display_name), ''), 'Player ' || p_telegram_id::TEXT);
        IF length(v_name) > 64 THEN
            v_name := substring(v_name FROM 1 FOR 64);
        END IF;

        INSERT INTO public.leaderboard_players (
            telegram_id,
            telegram_chat_id,
            player_code,
            display_name,
            best_score,
            has_submitted_score,
            created_at,
            updated_at
        ) VALUES (
            p_telegram_id,
            p_chat_id,
            v_code,
            v_name,
            0,
            FALSE,
            now(),
            now()
        )
        RETURNING * INTO v_player;
    END IF;

    RETURN QUERY
    SELECT 
        v_player.id,
        v_player.player_code,
        v_player.display_name,
        v_player.telegram_chat_id;
END;
$$;

REVOKE ALL ON FUNCTION public.register_telegram_chat(BIGINT, BIGINT, TEXT) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.register_telegram_chat(BIGINT, BIGINT, TEXT) TO service_role;


-- 5. RPC: Record notification delivery status and error diagnostics with mandatory fencing token check
DROP FUNCTION IF EXISTS public.record_notification_result(UUID, TEXT, TEXT);
DROP FUNCTION IF EXISTS public.record_notification_result(UUID, TEXT, TEXT, UUID);
DROP FUNCTION IF EXISTS public.record_notification_result(UUID, TEXT, UUID, TEXT);

CREATE OR REPLACE FUNCTION public.record_notification_result(
    p_event_id UUID,
    p_status TEXT,
    p_claim_token UUID,
    p_error TEXT DEFAULT NULL
)
RETURNS BOOLEAN
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_updated_rows INT;
BEGIN
    -- Fencing token is strictly mandatory: NULL token or mismatch must update zero rows and return false
    IF p_claim_token IS NULL THEN
        RETURN FALSE;
    END IF;

    UPDATE public.score_notification_events
    SET status = p_status,
        error_message = p_error,
        updated_at = now(),
        sent_at = CASE WHEN p_status = 'sent' THEN now() ELSE sent_at END
    WHERE id = p_event_id
      AND claim_token = p_claim_token;

    GET DIAGNOSTICS v_updated_rows = ROW_COUNT;
    RETURN v_updated_rows > 0;
END;
$$;

REVOKE ALL ON FUNCTION public.record_notification_result(UUID, TEXT, UUID, TEXT) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.record_notification_result(UUID, TEXT, UUID, TEXT) TO service_role;


-- 6. RPC: Outbox Recovery Worker - Claim pending, expired processing, or failed notifications with SKIP LOCKED concurrency guard
DROP FUNCTION IF EXISTS public.claim_pending_notifications(INT, INT);
DROP FUNCTION IF EXISTS public.claim_pending_notifications(INT, INT, INT, INT);

CREATE OR REPLACE FUNCTION public.claim_pending_notifications(
    p_batch_size INT DEFAULT 10,
    p_stale_interval_seconds INT DEFAULT 60,
    p_lease_duration_seconds INT DEFAULT 120,
    p_max_retries INT DEFAULT 3
)
RETURNS TABLE (
    event_id UUID,
    player_id UUID,
    telegram_id BIGINT,
    telegram_chat_id BIGINT,
    score INTEGER,
    global_rank BIGINT,
    player_code VARCHAR(7),
    claim_token UUID
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    -- 1. Mark expired 'processing' events that have reached/exceeded max retries as permanently failed
    -- (ONLY after lease expiration; actively processing events within lease are never touched)
    UPDATE public.score_notification_events
    SET status = 'failed',
        error_message = COALESCE(error_message, 'MAX_RETRIES_EXCEEDED: Lease expired after reaching maximum retry limit (' || retry_count || '/' || p_max_retries || ')'),
        updated_at = now()
    WHERE status = 'processing'
      AND updated_at < now() - (p_lease_duration_seconds || ' seconds')::INTERVAL
      AND retry_count >= p_max_retries;

    -- 2. Mark exhausted or abandoned 'pending' events as permanently failed (exceeded retries or older than 24h)
    UPDATE public.score_notification_events
    SET status = 'failed',
        error_message = COALESCE(
            error_message, 
            CASE 
                WHEN retry_count >= p_max_retries THEN 'MAX_RETRIES_EXCEEDED: Pending event reached maximum retry limit (' || retry_count || '/' || p_max_retries || ')'
                ELSE 'EXPIRED_ABANDONED: Pending notification unhandled after 24 hours'
            END
        ),
        updated_at = now()
    WHERE status = 'pending'
      AND (
          retry_count >= p_max_retries
          OR created_at < now() - INTERVAL '24 hours'
      );

    -- 3. Atomically select and lock eligible events using FOR UPDATE SKIP LOCKED
    -- (Include all necessary player/chat columns directly in CTE to avoid invalid UPDATE FROM JOIN target references)
    RETURN QUERY
    WITH claimable AS (
        SELECT 
            sne.id AS event_id,
            sne.player_id,
            sne.telegram_id,
            sne.score,
            sne.global_rank,
            lp.telegram_chat_id,
            lp.player_code
        FROM public.score_notification_events sne
        JOIN public.leaderboard_players lp ON lp.id = sne.player_id
        WHERE lp.telegram_chat_id IS NOT NULL
          AND sne.retry_count < p_max_retries
          AND (
            -- A: Stale pending event (unhandled by submit-score within initial timeout, within 24h)
            (sne.status = 'pending' 
             AND sne.created_at < now() - (p_stale_interval_seconds || ' seconds')::INTERVAL
             AND sne.created_at >= now() - INTERVAL '24 hours')
            OR
            -- B: Expired processing lease (worker crashed, timed out, or hung; within 24h)
            (sne.status = 'processing' 
             AND sne.updated_at < now() - (p_lease_duration_seconds || ' seconds')::INTERVAL
             AND sne.created_at >= now() - INTERVAL '24 hours')
            OR
            -- C: Failed event eligible for retry (retry backoff: at least 30s since last failure, within 24h)
            (sne.status = 'failed' 
             AND sne.updated_at < now() - INTERVAL '30 seconds' 
             AND sne.created_at >= now() - INTERVAL '24 hours')
          )
        ORDER BY sne.created_at ASC
        LIMIT p_batch_size
        FOR UPDATE OF sne SKIP LOCKED
    )
    UPDATE public.score_notification_events sne
    SET status = 'processing',
        retry_count = sne.retry_count + 1,
        claim_token = gen_random_uuid(),
        updated_at = now()
    FROM claimable c
    WHERE sne.id = c.event_id
    RETURNING 
        sne.id AS event_id,
        c.player_id,
        c.telegram_id,
        c.telegram_chat_id,
        c.score,
        c.global_rank,
        c.player_code,
        sne.claim_token;
END;
$$;

REVOKE ALL ON FUNCTION public.claim_pending_notifications(INT, INT, INT, INT) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.claim_pending_notifications(INT, INT, INT, INT) TO service_role;


-- 7. Updated RPC: submit_score_atomic with atomic personal best detection and outbox event creation
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
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;

        v_is_new_pb := TRUE;
    ELSIF p_submitted_score > v_player.best_score THEN
        UPDATE public.leaderboard_players
        SET best_score = p_submitted_score,
            updated_at = now()
        WHERE id = v_player.id
        RETURNING * INTO v_player;

        v_is_new_pb := TRUE;
    ELSE
        v_is_new_pb := FALSE;
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
GRANT EXECUTE ON FUNCTION public.submit_score_atomic(BIGINT, INTEGER) TO service_role;
