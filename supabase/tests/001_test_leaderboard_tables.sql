-- ==============================================================================
-- GitJumps Leaderboard — Phase 1 Test Script
-- Run this in Supabase SQL Editor to verify all Phase 1 constraints and functions
-- ==============================================================================

DO $$
DECLARE
    v_p1 RECORD;
    v_p2 RECORD;
    v_p3 RECORD;
    v_p4 RECORD;
    v_res RECORD;
    v_count INT;
BEGIN
    RAISE NOTICE '>>> STARTING PHASE 1 LEADERBOARD TESTS <<<';

    -- Test 1: Register Player 1
    SELECT * INTO v_p1 FROM public.get_or_create_player(1111111, 'Alice');
    ASSERT v_p1.player_code ~ '^GJ-[0-9]{4}$', 'Player 1 code format invalid: ' || v_p1.player_code;
    ASSERT v_p1.is_new_player = TRUE, 'Player 1 should be marked as new player';
    RAISE NOTICE 'Test 1 Passed: Alice registered with %', v_p1.player_code;

    -- Test 2: Register same player (Player 1) with updated name
    SELECT * INTO v_p2 FROM public.get_or_create_player(1111111, 'Alice Wonder');
    ASSERT v_p2.player_code = v_p1.player_code, 'Returning player code must match initial code';
    ASSERT v_p2.display_name = 'Alice Wonder', 'Display name was not refreshed';
    ASSERT v_p2.is_new_player = FALSE, 'Existing player must not be marked as new';
    RAISE NOTICE 'Test 2 Passed: Alice Wonder returned existing code %', v_p2.player_code;

    -- Test 3: Register Player 2 and Player 3 (Unique Code Check)
    SELECT * INTO v_p3 FROM public.get_or_create_player(2222222, 'Bob');
    SELECT * INTO v_p4 FROM public.get_or_create_player(3333333, 'Charlie');
    ASSERT v_p3.player_code <> v_p1.player_code, 'Player codes must be unique (Bob vs Alice)';
    ASSERT v_p4.player_code <> v_p3.player_code, 'Player codes must be unique (Charlie vs Bob)';
    RAISE NOTICE 'Test 3 Passed: Bob (%) and Charlie (%) have distinct codes', v_p3.player_code, v_p4.player_code;

    -- Test 4: Submit initial score for Bob (Score: 100)
    SELECT * INTO v_res FROM public.submit_score_atomic(2222222, 100);
    ASSERT v_res.best_score = 100, 'Best score should be 100';
    ASSERT v_res.is_new_high_score = TRUE, 'Should be flagged as new high score';
    RAISE NOTICE 'Test 4 Passed: Bob high score updated to 100';

    -- Test 5: Submit LOWER score for Bob (Score: 50 -> must NOT overwrite 100)
    SELECT * INTO v_res FROM public.submit_score_atomic(2222222, 50);
    ASSERT v_res.best_score = 100, 'Lower score must not overwrite higher personal best';
    ASSERT v_res.is_new_high_score = FALSE, 'Lower score should not be flagged as new high score';
    RAISE NOTICE 'Test 5 Passed: Lower score 50 did not overwrite 100';

    -- Test 6: Submit HIGHER score for Bob (Score: 250)
    SELECT * INTO v_res FROM public.submit_score_atomic(2222222, 250);
    ASSERT v_res.best_score = 250, 'Higher score must update personal best';
    ASSERT v_res.is_new_high_score = TRUE, 'Higher score must be flagged as new high score';
    RAISE NOTICE 'Test 6 Passed: Higher score 250 updated successfully';

    -- Test 7: Verify medals and ranking order
    PERFORM public.submit_score_atomic(3333333, 500); -- Charlie -> 500 (Rank 1 🥇)
    PERFORM public.submit_score_atomic(1111111, 250); -- Alice -> 250 (Rank 2 / 3 tiebreak)

    SELECT count(*) INTO v_count FROM public.get_top_leaderboard(10);
    ASSERT v_count >= 3, 'Top leaderboard should have at least 3 players';

    -- Check first rank is medal 🥇
    SELECT rank_display INTO v_res FROM public.get_top_leaderboard(1) WHERE rank = 1;
    ASSERT v_res.rank_display = '🥇', 'Rank 1 must have gold medal emoji';
    RAISE NOTICE 'Test 7 Passed: Leaderboard medals and ranks verified successfully';

    -- Clean up test records
    DELETE FROM public.leaderboard_players WHERE telegram_id IN (1111111, 2222222, 3333333);
    RAISE NOTICE '>>> ALL PHASE 1 DATABASE TESTS PASSED SUCCESSFULLY! <<<';
END $$;

