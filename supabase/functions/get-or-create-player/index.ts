import { handleCors, corsHeaders } from '../_shared/cors.ts';
import { verifyTelegramInitData } from '../_shared/telegramAuth.ts';
import { getServiceSupabaseClient } from '../_shared/supabaseClient.ts';

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
    const botToken = Deno.env.get('TELEGRAM_BOT_TOKEN') || '';
    const allowDevAuth = Deno.env.get('ALLOW_DEV_AUTH') === 'true';

    // 1. Verify Telegram Identity
    const verified = await verifyTelegramInitData(initData, botToken, allowDevAuth);

    // 2. Query / Upsert in Supabase using the atomic stored procedure
    const supabase = getServiceSupabaseClient();
    const { data, error } = await supabase.rpc('get_or_create_player', {
      p_telegram_id: verified.telegramId,
      p_display_name: verified.displayName,
    });

    if (error) {
      console.error('Database RPC error in get_or_create_player:', error);
      return new Response(
        JSON.stringify({ success: false, error: error.message || 'DATABASE_ERROR' }),
        { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const playerRecord = Array.isArray(data) && data.length > 0 ? data[0] : data;

    return new Response(
      JSON.stringify({
        success: true,
        player: {
          player_code: playerRecord.player_code,
          display_name: playerRecord.display_name,
          best_score: Number(playerRecord.best_score),
          global_rank: Number(playerRecord.global_rank),
          is_new_player: Boolean(playerRecord.is_new_player),
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

