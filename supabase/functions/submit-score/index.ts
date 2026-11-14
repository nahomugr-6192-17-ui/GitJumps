import { handleCors, corsHeaders } from '../_shared/cors.ts';
import { verifyTelegramInitData } from '../_shared/telegramAuth.ts';
import { getServiceSupabaseClient } from '../_shared/supabaseClient.ts';

// In-memory rate limiting map for Edge Function instances (telegramId -> lastSubmissionTimestampMs)
const lastSubmissionMap = new Map<number, number>();
const MIN_SUBMISSION_INTERVAL_MS = 2000; // 2 seconds minimum between finished games
const MAX_REALISTIC_SCORE = 100000;
const TELEGRAM_API_TIMEOUT_MS = 4000;

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

    // 4. Submit Score Atomically via RPC (database commit occurs before delivery)
    const supabase = getServiceSupabaseClient();
    const { data, error } = await supabase.rpc('submit_score_atomic', {
      p_telegram_id: verified.telegramId,
      p_submitted_score: rawScore,
    });

    if (error) {
      console.error('[submit-score] Database RPC error in submit_score_atomic:', error.message);
      return new Response(
        JSON.stringify({ success: false, error: error.message || 'DATABASE_ERROR' }),
        { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const result = Array.isArray(data) && data.length > 0 ? data[0] : data;
    if (!result) {
      throw new Error('DATABASE_ERROR: No player record returned');
    }

    const isNewPersonalBest = Boolean(result.is_new_personal_best);
    const eventId = result.notification_event_id;
    const claimToken = result.claim_token ? String(result.claim_token) : null;
    const chatId = result.telegram_chat_id ? Number(result.telegram_chat_id) : null;
    const bestScore = Number(result.best_score);
    const globalRank = Number(result.global_rank);
    const playerCode = String(result.player_code);

    // 5. Trigger Telegram Personal Best Notification asynchronously if applicable
    if (isNewPersonalBest && eventId && claimToken) {
      if (chatId && botToken) {
        const deliveryPromise = deliverScoreNotification(
          supabase,
          botToken,
          chatId,
          eventId,
          claimToken,
          bestScore,
          globalRank,
          playerCode
        ).catch((notifyErr) => {
          console.error(
            '[submit-score] Background notification delivery error:',
            notifyErr instanceof Error ? notifyErr.message : notifyErr
          );
        });

        // Register background task with EdgeRuntime to keep isolate alive without blocking response
        const edgeRuntime = (globalThis as any).EdgeRuntime;
        if (edgeRuntime && typeof edgeRuntime.waitUntil === 'function') {
          edgeRuntime.waitUntil(deliveryPromise);
        }
      } else if (!chatId) {
        console.log(`[submit-score] Score ${bestScore} is personal best for ${playerCode}, but no private chat is registered. Skipped notification.`);
      } else if (!botToken) {
        console.warn(`[submit-score] TELEGRAM_BOT_TOKEN not configured. Cannot send notification for ${playerCode}.`);
      }
    }

    // 6. Return response immediately to Unity game client (zero gameplay latency)
    return new Response(
      JSON.stringify({
        success: true,
        player: {
          player_code: result.player_code,
          display_name: result.display_name,
          best_score: bestScore,
          global_rank: globalRank,
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

/**
 * Sends the Telegram Bot API sendMessage request and updates the notification event status.
 * Never throws to the caller to ensure gameplay is never disrupted.
 */
async function deliverScoreNotification(
  supabase: any,
  botToken: string,
  chatId: number,
  eventId: string,
  claimToken: string,
  bestScore: number,
  globalRank: number,
  playerCode: string
): Promise<void> {
  const messageText = 
`🎉 NEW PERSONAL BEST!

🏆 New best score: ${bestScore}
🌍 Global leaderboard position: #${globalRank}
🆔 Player code: ${playerCode}

Keep jumping and climb the leaderboard!`;

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), TELEGRAM_API_TIMEOUT_MS);

  try {
    const response = await fetch(`https://api.telegram.org/bot${botToken}/sendMessage`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        chat_id: chatId,
        text: messageText,
      }),
      signal: controller.signal,
    });

    clearTimeout(timeoutId);

    if (response.ok) {
      console.log(`[submit-score] Successfully delivered personal best notification to chat ${chatId} (${playerCode})`);
      await supabase.rpc('record_notification_result', {
        p_event_id: eventId,
        p_status: 'sent',
        p_claim_token: claimToken,
        p_error: null,
      });
    } else {
      const errBody = await response.text().catch(() => '');
      const safeError = `HTTP_${response.status}: ${errBody.slice(0, 120)}`;
      console.warn(`[submit-score] Telegram sendMessage returned error status: ${safeError}`);

      await supabase.rpc('record_notification_result', {
        p_event_id: eventId,
        p_status: 'failed',
        p_claim_token: claimToken,
        p_error: safeError,
      });
    }
  } catch (fetchErr: unknown) {
    clearTimeout(timeoutId);
    const errorMsg = fetchErr instanceof Error ? fetchErr.message : 'UNKNOWN_FETCH_ERROR';
    console.warn(`[submit-score] Telegram network failure: ${errorMsg}`);

    try {
      await supabase.rpc('record_notification_result', {
        p_event_id: eventId,
        p_status: 'failed',
        p_claim_token: claimToken,
        p_error: errorMsg.slice(0, 150),
      });
    } catch (dbErr) {
      console.error('[submit-score] Failed to record notification failure status:', dbErr);
    }
  }
}
