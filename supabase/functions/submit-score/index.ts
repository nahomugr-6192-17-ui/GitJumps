import { handleCors, corsHeaders } from '../_shared/cors.ts';
import { verifyTelegramInitData } from '../_shared/telegramAuth.ts';
import { getServiceSupabaseClient } from '../_shared/supabaseClient.ts';

// In-memory rate limiting map for Edge Function instances (telegramId -> lastSubmissionTimestampMs)
const lastSubmissionMap = new Map<number, number>();
const MIN_SUBMISSION_INTERVAL_MS = 2500; // 2.5 seconds minimum between finished games
const MAX_REALISTIC_SCORE = 100000;

Deno.serve(async (req: Request) => {
  const corsResponse = handleCors(req);
  if (corsResponse) return corsResponse;

  if (req.method !== 'POST') {
    return new Response(
      JSON.stringify({ success: false, error: 'METHOD_NOT_ALLOWED: Use POST' }),
      { status: 405, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  }

  try {
    const body = await req.json().catch(() => ({}));
    const initData = body.initData || req.headers.get('x-telegram-init-data') || '';
    const rawScore = body.score;
    const botToken = Deno.env.get('TELEGRAM_BOT_TOKEN') || '';
    const allowDevAuth = Deno.env.get('ALLOW_DEV_AUTH') === 'true';

    // 1. Verify Telegram Identity
    const verified = await verifyTelegramInitData(initData, botToken, allowDevAuth);

    // 2. Validate Score
    if (typeof rawScore !== 'number' || !Number.isInteger(rawScore)) {
      return new Response(
        JSON.stringify({ success: false, error: 'INVALID_SCORE: Score must be an integer' }),
        { status: 400, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    if (rawScore < 0) {
      return new Response(
        JSON.stringify({ success: false, error: 'INVALID_SCORE: Score cannot be negative' }),
        { status: 400, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    if (rawScore > MAX_REALISTIC_SCORE) {
      return new Response(
        JSON.stringify({ success: false, error: 'INVALID_SCORE: Score exceeds maximum allowable limit' }),
        { status: 400, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    // 3. Rate Limiting Check
    const now = Date.now();
    const lastTime = lastSubmissionMap.get(verified.telegramId) || 0;
    if (now - lastTime < MIN_SUBMISSION_INTERVAL_MS) {
      return new Response(
        JSON.stringify({
          success: false,
          error: 'RATE_LIMITED: Submissions are arriving too quickly. Please wait a moment.',
        }),
        { status: 429, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }
    lastSubmissionMap.set(verified.telegramId, now);

    // 4. Submit Score Atomically via RPC (guarantees lower scores never overwrite personal best)
    const supabase = getServiceSupabaseClient();
    const { data, error } = await supabase.rpc('submit_score_atomic', {
      p_telegram_id: verified.telegramId,
      p_submitted_score: rawScore,
    });

    if (error) {
      console.error('Database RPC error in submit_score_atomic:', error);
      return new Response(
        JSON.stringify({ success: false, error: error.message || 'DATABASE_ERROR' }),
        { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const result = Array.isArray(data) && data.length > 0 ? data[0] : data;

    return new Response(
      JSON.stringify({
        success: true,
        player: {
          player_code: result.player_code,
          display_name: result.display_name,
          best_score: Number(result.best_score),
          global_rank: Number(result.global_rank),
          is_new_high_score: Boolean(result.is_new_high_score),
        },
      }),
      { status: 200, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'INTERNAL_SERVER_ERROR';
    const status = message.includes('AUTHENTICATION_FAILED') || message.includes('INVALID_INIT_DATA') ? 401 : 400;

    return new Response(
      JSON.stringify({ success: false, error: message }),
      { status, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  }
});

