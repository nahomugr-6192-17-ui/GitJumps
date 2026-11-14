import { handleCors, corsHeaders } from '../_shared/cors.ts';
import { getServiceSupabaseClient } from '../_shared/supabaseClient.ts';

interface TelegramUser {
  id: number;
  is_bot?: boolean;
  first_name: string;
  last_name?: string;
  username?: string;
}

interface TelegramChat {
  id: number;
  type: string;
  first_name?: string;
  last_name?: string;
  username?: string;
}

interface TelegramMessage {
  message_id: number;
  from?: TelegramUser;
  chat: TelegramChat;
  date: number;
  text?: string;
}

interface TelegramUpdate {
  update_id: number;
  message?: TelegramMessage;
}

Deno.serve(async (req: Request) => {
  const corsResponse = handleCors(req);
  if (corsResponse) return corsResponse;

  // Management endpoint: GET with service_role authorization
  if (req.method === 'GET') {
    const authHeader = req.headers.get('authorization') || '';
    const apikeyHeader = req.headers.get('apikey') || '';
    const serviceKey = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') || '';
    
    // Check direct equality or valid service_role JWT payload for this project
    let isAuthorized = false;
    if (serviceKey && (authHeader === `Bearer ${serviceKey}` || apikeyHeader === serviceKey)) {
      isAuthorized = true;
    } else {
      const rawJwt = (authHeader.startsWith('Bearer ') ? authHeader.slice(7) : apikeyHeader).trim();
      try {
        const parts = rawJwt.split('.');
        if (parts.length === 3) {
          const payload = JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')));
          if (payload.role === 'service_role' && payload.ref === 'suqrmebduvkeqhshasff') {
            isAuthorized = true;
          }
        }
      } catch {}
    }

    if (!isAuthorized) {
      return new Response(JSON.stringify({ ok: false, error: 'UNAUTHORIZED: Service role authorization required' }), {
        status: 401,
        headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      });
    }

    const botToken = Deno.env.get('TELEGRAM_BOT_TOKEN') || '';
    const webhookSecret = Deno.env.get('TELEGRAM_WEBHOOK_SECRET') || '';
    const supabaseUrl = Deno.env.get('SUPABASE_URL') || '';
    const url = new URL(req.url);
    const action = url.searchParams.get('action') || 'getWebhookInfo';

    if (!botToken) {
      return new Response(JSON.stringify({ ok: false, error: 'TELEGRAM_BOT_TOKEN not configured on server' }), {
        status: 500,
        headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      });
    }

    if (action === 'getWebhookInfo') {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 6000);
      try {
        const resp = await fetch(`https://api.telegram.org/bot${botToken}/getWebhookInfo`, {
          signal: controller.signal,
        });
        clearTimeout(timeoutId);
        const info = await resp.json().catch(() => ({}));
        return new Response(JSON.stringify({ ok: true, webhook_info: info.result || info }), {
          status: 200,
          headers: { ...corsHeaders, 'Content-Type': 'application/json' },
        });
      } catch (err: unknown) {
        clearTimeout(timeoutId);
        return new Response(JSON.stringify({ ok: false, error: err instanceof Error ? err.message : String(err) }), {
          status: 504,
          headers: { ...corsHeaders, 'Content-Type': 'application/json' },
        });
      }
    }

    if (action === 'setWebhook') {
      if (!webhookSecret) {
        return new Response(JSON.stringify({ ok: false, error: 'TELEGRAM_WEBHOOK_SECRET not configured on server' }), {
          status: 500,
          headers: { ...corsHeaders, 'Content-Type': 'application/json' },
        });
      }

      // Inspect existing webhook before changing anything to prevent overwriting unrelated services
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 6000);
      try {
        const checkResp = await fetch(`https://api.telegram.org/bot${botToken}/getWebhookInfo`, {
          signal: controller.signal,
        });
        const checkInfo = await checkResp.json().catch(() => ({}));
        const existingUrl = (checkInfo.result?.url || '').trim();
        const targetEndpoint = `${supabaseUrl.replace(/\/$/, '')}/functions/v1/telegram-webhook`;

        // Conflict detection: if existing webhook points to an unrelated service, require explicit force
        const force = url.searchParams.get('force') === 'true';
        if (existingUrl && existingUrl !== targetEndpoint && !force) {
          clearTimeout(timeoutId);
          return new Response(JSON.stringify({
            ok: false,
            conflict: true,
            existing_url: existingUrl,
            target_url: targetEndpoint,
            message: 'Existing webhook points to a different URL. Confirmation required before overwriting.',
          }), {
            status: 409,
            headers: { ...corsHeaders, 'Content-Type': 'application/json' },
          });
        }

        // Register the endpoint with Telegram using the configured webhook secret
        const setResp = await fetch(`https://api.telegram.org/bot${botToken}/setWebhook`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            url: targetEndpoint,
            secret_token: webhookSecret,
            allowed_updates: ['message'],
          }),
          signal: controller.signal,
        });
        clearTimeout(timeoutId);
        const setResult = await setResp.json().catch(() => ({}));

        return new Response(JSON.stringify({
          ok: setResult.ok === true,
          target_url: targetEndpoint,
          telegram_result: setResult,
        }), {
          status: setResp.ok ? 200 : 500,
          headers: { ...corsHeaders, 'Content-Type': 'application/json' },
        });
      } catch (err: unknown) {
        clearTimeout(timeoutId);
        return new Response(JSON.stringify({ ok: false, error: err instanceof Error ? err.message : String(err) }), {
          status: 504,
          headers: { ...corsHeaders, 'Content-Type': 'application/json' },
        });
      }
    }

    return new Response(JSON.stringify({ ok: false, error: 'INVALID_ACTION' }), {
      status: 400,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
    });
  }

  if (req.method !== 'POST') {
    return new Response(JSON.stringify({ ok: false, error: 'METHOD_NOT_ALLOWED' }), {
      status: 405,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
    });
  }

  try {
    const webhookSecret = Deno.env.get('TELEGRAM_WEBHOOK_SECRET');
    const botToken = Deno.env.get('TELEGRAM_BOT_TOKEN') || '';

    // 1. Mandatory Webhook Secret Verification (Fail-Closed Security)
    if (!webhookSecret) {
      console.error('[telegram-webhook] Configuration error: TELEGRAM_WEBHOOK_SECRET is not set on the server. Failing closed.');
      return new Response(
        JSON.stringify({ ok: false, error: 'SERVER_CONFIG_ERROR: Webhook secret not configured' }),
        { status: 500, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const incomingSecret = req.headers.get('x-telegram-bot-api-secret-token');
    if (incomingSecret !== webhookSecret) {
      console.warn('[telegram-webhook] Unauthorized access attempt: Secret token mismatch');
      return new Response(
        JSON.stringify({ ok: false, error: 'UNAUTHORIZED: Invalid webhook secret token' }),
        { status: 401, headers: { ...corsHeaders, 'Content-Type': 'application/json' } }
      );
    }

    const update: TelegramUpdate = await req.json().catch(() => ({ update_id: 0 }));
    const message = update.message;

    // 2. Validate Chat & Sender Integrity
    // In Telegram, a genuine 1-on-1 private bot chat always has type == 'private' AND chat.id == from.id
    if (!message || !message.from || !message.chat) {
      return new Response(JSON.stringify({ ok: true, ignored: true }), {
        status: 200,
        headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      });
    }

    if (message.chat.type !== 'private' || message.chat.id !== message.from.id) {
      console.warn(`[telegram-webhook] Rejected non-private or mismatched update: chat.type=${message.chat.type}, chat.id=${message.chat.id}, from.id=${message.from.id}`);
      return new Response(JSON.stringify({ ok: true, ignored: true }), {
        status: 200,
        headers: { ...corsHeaders, 'Content-Type': 'application/json' },
      });
    }

    const telegramId = message.from.id;
    const chatId = message.chat.id;
    const firstName = (message.from.first_name || '').trim();
    const lastName = (message.from.last_name || '').trim();
    const displayName = [firstName, lastName].filter(Boolean).join(' ') || `Player ${telegramId}`;

    console.log(`[telegram-webhook] Registering verified private chat for player ${telegramId} (chat ${chatId})`);

    // 3. Register Chat in Database via Service Role
    const supabase = getServiceSupabaseClient();
    const { data, error } = await supabase.rpc('register_telegram_chat', {
      p_telegram_id: telegramId,
      p_chat_id: chatId,
      p_display_name: displayName,
    });

    if (error) {
      console.error('[telegram-webhook] Error registering chat in database:', error.message);
    } else {
      const playerRec = Array.isArray(data) && data.length > 0 ? data[0] : data;
      const playerCode = playerRec?.player_code || '';

      // 4. Send welcome confirmation reply on /start
      const text = (message.text || '').trim();
      if (text.startsWith('/start') && botToken) {
        await sendWelcomeMessage(botToken, chatId, playerCode);
      }
    }

    return new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
    });
  } catch (err: unknown) {
    const errorMsg = err instanceof Error ? err.message : 'INTERNAL_SERVER_ERROR';
    console.error('[telegram-webhook] Unhandled exception:', errorMsg);

    // Return 200 to Telegram to avoid webhook retry loops on unhandled server error
    return new Response(JSON.stringify({ ok: false, error: errorMsg }), {
      status: 200,
      headers: { ...corsHeaders, 'Content-Type': 'application/json' },
    });
  }
});

async function sendWelcomeMessage(botToken: string, chatId: number, playerCode: string): Promise<void> {
  const codeDisplay = playerCode ? `\n🆔 Your Player Code: <b>${playerCode}</b>` : '';
  const messageText = 
`🎉 <b>Welcome to GitJumps!</b> 🐱
${codeDisplay}

Your private notifications are active! You will receive a message here whenever you achieve a <b>New Personal Best</b> score.

Keep jumping and climb the leaderboard! 🚀`;

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 4000);

  try {
    const res = await fetch(`https://api.telegram.org/bot${botToken}/sendMessage`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        chat_id: chatId,
        text: messageText,
        parse_mode: 'HTML',
      }),
      signal: controller.signal,
    });

    if (!res.ok) {
      const errText = await res.text().catch(() => '');
      console.warn(`[telegram-webhook] Welcome message failed (${res.status}): ${errText}`);
    }
  } catch (err: unknown) {
    console.warn('[telegram-webhook] Welcome message network error:', err instanceof Error ? err.message : err);
  } finally {
    clearTimeout(timeoutId);
  }
}
