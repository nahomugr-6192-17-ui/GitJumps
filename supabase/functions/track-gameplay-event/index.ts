import { handleCors, corsHeaders } from '../_shared/cors.ts';
import { verifyTelegramInitData } from '../_shared/telegramAuth.ts';
import { getServiceSupabaseClient } from '../_shared/supabaseClient.ts';

const lastEventMap = new Map<number, number>();
const MIN_EVENT_INTERVAL_MS = 500; // 500ms debounce

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
    const eventName = body.event_name || 'game_started';
    const sessionId = body.session_id || null;
    const metadata = body.metadata || {};

    if (!['game_started', 'game_over'].includes(eventName)) {
      return new Response(
        JSON.stringify({ success: false, error: 'INVALID_EVENT: Only game_started and game_over can be directly tracked' }),
        { status: 400, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const botToken = Deno.env.get('TELEGRAM_BOT_TOKEN') || '';
    const allowDevAuth = Deno.env.get('ALLOW_DEV_AUTH') === 'true';

    // Verify Telegram InitData
    const verified = await verifyTelegramInitData(initData, botToken, allowDevAuth);

    // Debounce
    const now = Date.now();
    const lastTime = lastEventMap.get(verified.telegramId) || 0;
    if (now - lastTime < MIN_EVENT_INTERVAL_MS) {
      return new Response(
        JSON.stringify({ success: true, debounced: true }),
        { status: 200, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }
    lastEventMap.set(verified.telegramId, now);

    const supabase = getServiceSupabaseClient();
    const { data, error } = await supabase.rpc('track_gameplay_event', {
      p_telegram_id: verified.telegramId,
      p_event_name: eventName,
      p_session_id: sessionId,
      p_score: null,
      p_metadata: metadata,
    });

    if (error) {
      console.error('[track-gameplay-event] RPC error:', error.message);
      return new Response(
        JSON.stringify({ success: false, error: error.message }),
        { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    return new Response(
      JSON.stringify({
        success: true,
        data: Array.isArray(data) && data.length > 0 ? data[0] : data,
      }),
      { status: 200, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  } catch (err: any) {
    console.error('[track-gameplay-event] Unexpected error:', err.message);
    const isAuthError = err.message && (
      err.message.includes('INVALID_') ||
      err.message.includes('MISSING_') ||
      err.message.includes('AUTH_DATE_EXPIRED')
    );
    return new Response(
      JSON.stringify({ success: false, error: err.message || 'INTERNAL_ERROR' }),
      { status: isAuthError ? 401 : 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  }
});

