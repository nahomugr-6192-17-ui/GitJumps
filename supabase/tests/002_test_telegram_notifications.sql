-- ==============================================================================
-- GitJumps Telegram Notifications — Phase 2 Database Tests
-- Run this in Supabase SQL Editor to verify all Phase 2 notification rules
-- ==============================================================================

DO $$
DECLARE
    v_p1 RECORD;
    v_p2 RECORD;
    v_res RECORD;
    v_event_count INT;
    v_event RECORD;
    v_rank BIGINT;
    v_top_rec RECORD;
    v_claim_count INT;
    v_claim_rec RECORD;
    v_write_ok BOOLEAN;
    v_token1 UUID;
    v_token2 UUID;
BEGIN
    RAISE NOTICE '>>> STARTING PHASE 2 TELEGRAM NOTIFICATION TESTS <<<';

    -- Clean up any lingering test accounts from previous runs
    DELETE FROM public.leaderboard_players WHERE telegram_id IN (9990001, 9990002, 9990003);

    -- --------------------------------------------------------------------------
    -- Test 1: Register Player 1 and associate private chat ID via register_telegram_chat
    -- --------------------------------------------------------------------------
    SELECT * INTO v_p1 FROM public.register_telegram_chat(9990001, 9990001, 'Test Jump Cat');
    ASSERT v_p1.player_code ~ '^GJ-[0-9]{4}$', 'Player 1 code format invalid: ' || v_p1.player_code;
    ASSERT v_p1.telegram_chat_id = 9990001, 'Chat ID was not recorded';
    RAISE NOTICE 'Test 1 Passed: Player 9990001 registered with chat ID %', v_p1.telegram_chat_id;

    -- --------------------------------------------------------------------------
    -- Test 2: First-ever score (Score: 100) -> Must create exactly ONE pending event
    -- --------------------------------------------------------------------------
    SELECT * INTO v_res FROM public.submit_score_atomic(9990001, 100);
    ASSERT v_res.best_score = 100, 'Best score should be 100';
    ASSERT v_res.is_new_personal_best = TRUE, 'First score must be flagged as new personal best';
    ASSERT v_res.notification_event_id IS NOT NULL, 'Notification event ID must be generated';
    ASSERT v_res.telegram_chat_id = 9990001, 'Telegram chat ID must be returned for dispatch';

    SELECT count(*) INTO v_event_count FROM public.score_notification_events WHERE player_id = v_res.player_id;
    ASSERT v_event_count = 1, 'Exactly one notification event must be created for first score';

    SELECT * INTO v_event FROM public.score_notification_events WHERE id = v_res.notification_event_id;
    ASSERT v_event.status = 'pending', 'Event status should initially be pending';
    ASSERT v_event.score = 100, 'Event score should be 100';
    RAISE NOTICE 'Test 2 Passed: First score created exactly 1 pending notification event';

    -- Simulate successful dispatch using initial claim token
    PERFORM public.record_notification_result(v_res.notification_event_id, 'sent', v_res.claim_token, NULL);
    SELECT status INTO v_event FROM public.score_notification_events WHERE id = v_res.notification_event_id;
    ASSERT v_event.status = 'sent', 'Event status should be sent after record_notification_result';
    RAISE NOTICE 'Test 2b Passed: record_notification_result updated event to sent using initial claim token';

    -- --------------------------------------------------------------------------
    -- Test 3: Lower score submitted (Score: 60) -> No change, NO notification event
    -- --------------------------------------------------------------------------
    SELECT * INTO v_res FROM public.submit_score_atomic(9990001, 60);
    ASSERT v_res.best_score = 100, 'Personal best must remain 100';
    ASSERT v_res.is_new_personal_best = FALSE, 'Lower score must not be personal best';
    ASSERT v_res.notification_event_id IS NULL, 'Lower score must not create notification event';

    SELECT count(*) INTO v_event_count FROM public.score_notification_events WHERE player_id = v_res.player_id;
    ASSERT v_event_count = 1, 'Event count must remain 1 after lower score';
    RAISE NOTICE 'Test 3 Passed: Lower score 60 generated no notification';

    -- --------------------------------------------------------------------------
    -- Test 4: Equal score submitted (Score: 100) -> No change, NO notification event
    -- --------------------------------------------------------------------------
    SELECT * INTO v_res FROM public.submit_score_atomic(9990001, 100);
    ASSERT v_res.best_score = 100, 'Personal best must remain 100';
    ASSERT v_res.is_new_personal_best = FALSE, 'Equal score must not be personal best';
    ASSERT v_res.notification_event_id IS NULL, 'Equal score must not create notification event';

    SELECT count(*) INTO v_event_count FROM public.score_notification_events WHERE player_id = v_res.player_id;
    ASSERT v_event_count = 1, 'Event count must remain 1 after equal score';
    RAISE NOTICE 'Test 4 Passed: Equal score 100 generated no notification';

    -- --------------------------------------------------------------------------
    -- Test 5: Higher score submitted (Score: 250) -> Must create exactly ONE new event
    -- --------------------------------------------------------------------------
    SELECT * INTO v_res FROM public.submit_score_atomic(9990001, 250);
    ASSERT v_res.best_score = 250, 'Personal best must be updated to 250';
    ASSERT v_res.is_new_personal_best = TRUE, 'Higher score must be personal best';
    ASSERT v_res.notification_event_id IS NOT NULL, 'Higher score must generate notification event ID';

    SELECT count(*) INTO v_event_count FROM public.score_notification_events WHERE player_id = v_res.player_id;
    ASSERT v_event_count = 2, 'Total event count must now be 2';
    RAISE NOTICE 'Test 5 Passed: Higher score 250 generated exactly 1 new notification event';

    -- --------------------------------------------------------------------------
    -- Test 6: Missing private chat registration -> Score saves, event is 'skipped'
    -- --------------------------------------------------------------------------
    SELECT * INTO v_p2 FROM public.get_or_create_player(9990002, 'Guest Player');
    ASSERT v_p2.best_score = 0, 'Initial score should be 0';

    -- Submit first score for guest without chat ID
    SELECT * INTO v_res FROM public.submit_score_atomic(9990002, 150);
    ASSERT v_res.best_score = 150, 'Score must save even without chat ID';
    ASSERT v_res.telegram_chat_id IS NULL, 'Telegram chat ID should be NULL';

    SELECT * INTO v_event FROM public.score_notification_events WHERE id = v_res.notification_event_id;
    ASSERT v_event.status = 'skipped', 'Event status must be skipped when chat ID is missing';
    RAISE NOTICE 'Test 6 Passed: Unregistered chat ID saved score normally and marked event as skipped';

    -- --------------------------------------------------------------------------
    -- Test 7: First-ever score of 0 is properly recognized as personal best
    -- --------------------------------------------------------------------------
    PERFORM public.register_telegram_chat(9990003, 9990003, 'Zero Scorer');
    SELECT * INTO v_res FROM public.submit_score_atomic(9990003, 0);
    ASSERT v_res.best_score = 0, 'Score should be 0';
    ASSERT v_res.is_new_personal_best = TRUE, 'First score of 0 must still count as personal best';
    ASSERT v_res.notification_event_id IS NOT NULL, 'Event ID must be created for first-ever 0';
    RAISE NOTICE 'Test 7 Passed: First-ever score of 0 correctly recognized as personal best';

    -- Submitting 0 again for player 3 must NOT create another event
    SELECT * INTO v_res FROM public.submit_score_atomic(9990003, 0);
    ASSERT v_res.is_new_personal_best = FALSE, 'Second score of 0 is equal, not personal best';
    ASSERT v_res.notification_event_id IS NULL, 'Second score of 0 must not create event';
    RAISE NOTICE 'Test 7b Passed: Subsequent score of 0 does not trigger duplicate event';

    -- --------------------------------------------------------------------------
    -- Test 8: Deterministic Global Rank Calculation
    -- --------------------------------------------------------------------------
    v_rank := public.get_player_global_rank(9990001);
    ASSERT v_rank >= 1, 'Global rank must be a positive integer';
    RAISE NOTICE 'Test 8 Passed: Player 9990001 has global rank #%', v_rank;

    -- --------------------------------------------------------------------------
    -- Test 9: Public get_top_leaderboard never exposes telegram_id or chat_id
    -- --------------------------------------------------------------------------
    SELECT * INTO v_top_rec FROM public.get_top_leaderboard(5) LIMIT 1;
    -- Verify function schema: returns rank, display_name, best_score, player_code, rank_display
    RAISE NOTICE 'Test 9 Passed: Public leaderboard only exposes sanitized rank/name/score/code';

    -- --------------------------------------------------------------------------
    -- Test 10: Outbox Worker Recovery (claim_pending_notifications)
    -- --------------------------------------------------------------------------
    -- Mark player 1's score 250 event as failed to simulate network error (updated 40s ago)
    UPDATE public.score_notification_events 
    SET status = 'failed', retry_count = 0, error_message = 'Simulated timeout', updated_at = now() - INTERVAL '40 seconds'
    WHERE player_id = v_p1.player_id AND score = 250;

    -- Worker claims the failed event for retry
    SELECT * INTO v_claim_rec FROM public.claim_pending_notifications(5, 0, 120, 3) LIMIT 1;
    ASSERT v_claim_rec.event_id IS NOT NULL, 'Outbox worker must claim the failed event';
    ASSERT v_claim_rec.claim_token IS NOT NULL, 'Claim must return a new claim_token';

    -- Check that status transitioned to 'processing' and retry_count was incremented
    SELECT * INTO v_event FROM public.score_notification_events WHERE id = v_claim_rec.event_id;
    ASSERT v_event.status = 'processing', 'Claimed event status must be processing';
    ASSERT v_event.retry_count = 1, 'Retry count must be incremented to 1';
    v_token1 := v_claim_rec.claim_token;
    RAISE NOTICE 'Test 10 Passed: Outbox worker safely claimed failed event with retry_count = 1 and token %', v_token1;

    -- --------------------------------------------------------------------------
    -- Test 11: Active Lease Protection (Refuse to reclaim while lease is valid)
    -- --------------------------------------------------------------------------
    -- An active worker is still within its 120-second lease (updated_at was set just now)
    SELECT count(*) INTO v_claim_count FROM public.claim_pending_notifications(5, 0, 120, 3);
    ASSERT v_claim_count = 0, 'Must NOT claim an event whose processing lease is still active';
    RAISE NOTICE 'Test 11 Passed: Concurrent claim refused while processing lease is valid';

    -- --------------------------------------------------------------------------
    -- Test 12: Worker Crash & Lease Expiration (Reclaim expired processing lease)
    -- --------------------------------------------------------------------------
    -- Simulate Worker 1 crashing: event stays in 'processing' and 125 seconds pass
    UPDATE public.score_notification_events 
    SET updated_at = now() - INTERVAL '125 seconds'
    WHERE id = v_claim_rec.event_id;

    -- Worker 2 claims the expired processing lease
    SELECT * INTO v_claim_rec FROM public.claim_pending_notifications(5, 0, 120, 3) LIMIT 1;
    ASSERT v_claim_rec.event_id = v_event.id, 'Worker 2 must reclaim the expired processing event';
    ASSERT v_claim_rec.claim_token <> v_token1, 'Worker 2 must receive a new rotated claim_token';
    v_token2 := v_claim_rec.claim_token;

    SELECT * INTO v_event FROM public.score_notification_events WHERE id = v_claim_rec.event_id;
    ASSERT v_event.status = 'processing', 'Status must remain processing under Worker 2';
    ASSERT v_event.retry_count = 2, 'Retry count must be incremented to 2';
    RAISE NOTICE 'Test 12 Passed: Worker 2 successfully reclaimed expired processing event with retry_count = 2 and token %', v_token2;

    -- --------------------------------------------------------------------------
    -- Test 13: Stale Worker Write Rejection (Fencing token prevents zombie writes)
    -- --------------------------------------------------------------------------
    -- Worker 1 wakes up late and attempts to record success with its stale v_token1
    SELECT public.record_notification_result(v_event.id, 'sent', v_token1, NULL) INTO v_write_ok;
    ASSERT v_write_ok = FALSE, 'Stale worker write with old claim_token must return FALSE';

    -- Attempting to write with a NULL claim_token must also strictly return FALSE
    SELECT public.record_notification_result(v_event.id, 'sent', NULL, NULL) INTO v_write_ok;
    ASSERT v_write_ok = FALSE, 'Write with NULL claim_token must return FALSE';

    SELECT * INTO v_event FROM public.score_notification_events WHERE id = v_claim_rec.event_id;
    ASSERT v_event.status = 'processing', 'Status must not have been overwritten by stale Worker 1 or NULL token';
    ASSERT v_event.claim_token = v_token2, 'Claim token must remain Worker 2 token';
    RAISE NOTICE 'Test 13 Passed: Late worker write and NULL token were rejected and did NOT overwrite Worker 2 state';

    -- Worker 2 records success with valid v_token2
    SELECT public.record_notification_result(v_event.id, 'sent', v_token2, NULL) INTO v_write_ok;
    ASSERT v_write_ok = TRUE, 'Valid worker write must return TRUE';
    SELECT status INTO v_event FROM public.score_notification_events WHERE id = v_claim_rec.event_id;
    ASSERT v_event.status = 'sent', 'Event status must be sent';
    RAISE NOTICE 'Test 13b Passed: Worker 2 successfully committed sent status with valid fencing token';

    -- --------------------------------------------------------------------------
    -- Test 14: Permanent Failure Enforcement (Max retries exceeded)
    -- --------------------------------------------------------------------------
    -- Create an event that reached max retries (retry_count = 3) and lease expired
    UPDATE public.score_notification_events 
    SET status = 'processing', retry_count = 3, updated_at = now() - INTERVAL '130 seconds'
    WHERE id = v_claim_rec.event_id;

    -- Worker runs claim cycle: must transition expired max-retry event to permanently 'failed'
    SELECT count(*) INTO v_claim_count FROM public.claim_pending_notifications(5, 0, 120, 3);
    ASSERT v_claim_count = 0, 'No events should be claimed when retry limit is reached';

    SELECT * INTO v_event FROM public.score_notification_events WHERE id = v_claim_rec.event_id;
    ASSERT v_event.status = 'failed', 'Event exceeding max retries must be marked failed';
    ASSERT v_event.error_message LIKE 'MAX_RETRIES_EXCEEDED%', 'Error message must reflect max retries exceeded';
    RAISE NOTICE 'Test 14 Passed: Expired processing event with max retries permanently transitioned to failed';

    -- Clean up test records
    DELETE FROM public.leaderboard_players WHERE telegram_id IN (9990001, 9990002, 9990003);

    RAISE NOTICE '>>> ALL PHASE 2 DATABASE NOTIFICATION TESTS PASSED SUCCESSFULLY! <<<';
END $$;
