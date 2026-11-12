import { handleCors, corsHeaders } from '../_shared/cors.ts';
import { verifyTelegramInitData } from '../_shared/telegramAuth.ts';
import { getServiceSupabaseClient } from '../_shared/supabaseClient.ts';

Deno.serve(async (req: Request) => {
  const corsResponse = handleCors(req);
  if (corsResponse) return corsResponse;

  try {
    const url = new URL(req.url);
    const botToken = Deno.env.get('TELEGRAM_BOT_TOKEN') || '';
    const allowDevAuth = Deno.env.get('ALLOW_DEV_AUTH') === 'true';

    // Extract optional initData to identify requesting player
    let initData = req.headers.get('x-telegram-init-data') || url.searchParams.get('initData') || '';
    if (!initData && req.method === 'POST') {
      const body = await req.json().catch(() => ({}));
      initData = body.initData || '';
    }

    const supabase = getServiceSupabaseClient();

    // 1. Fetch Top 100 Leaderboard Entries via RPC
    const { data: leaderboardData, error: leaderboardError } = await supabase.rpc(
      'get_top_leaderboard',
      { p_limit: 100 }
    );

    if (leaderboardError) {
      console.error('Database RPC error in get_top_leaderboard:', leaderboardError);
      return new Response(
        JSON.stringify({ success: false, error: leaderboardError.message || 'DATABASE_ERROR' }),
        { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const entries = (leaderboardData || []).map((row: any) => ({
      rank: Number(row.rank),
      display_name: String(row.display_name),
      best_score: Number(row.best_score),
      rank_display: String(row.rank_display),
      player_code: String(row.player_code),
    }));

    // 2. If authenticated player info is provided, fetch their personal profile and global rank
    let currentPlayer = null;
    if (initData) {
      try {
        const verified = await verifyTelegramInitData(initData, botToken, allowDevAuth);

        const { data: playerData, error: playerError } = await supabase.rpc(
          'get_or_create_player',
          {
            p_telegram_id: verified.telegramId,
            p_display_name: verified.displayName,
          }
        );

        if (!playerError && playerData) {
          const rec = Array.isArray(playerData) ? playerData[0] : playerData;
          currentPlayer = {
            player_code: rec.player_code,
            display_name: rec.display_name,
            best_score: Number(rec.best_score),
            global_rank: Number(rec.global_rank),
          };
        }
      } catch (authErr) {
        // Soft fail on player lookup: still return public top 100
        console.warn('Could not authenticate player profile for leaderboard view:', authErr);
      }
    }

    return new Response(
      JSON.stringify({
        success: true,
        count: entries.length,
        leaderboard: entries,
        current_player: currentPlayer,
      }),
      { status: 200, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'INTERNAL_SERVER_ERROR';
    return new Response(
      JSON.stringify({ success: false, error: message }),
      { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
    );
  }
});

