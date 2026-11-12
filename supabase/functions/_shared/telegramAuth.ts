// Cryptographic verification of Telegram WebApp / Mini App initData
// Spec: https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app

export interface TelegramUser {
  id: number;
  first_name: string;
  last_name?: string;
  username?: string;
  language_code?: string;
  is_premium?: boolean;
}

export interface VerifiedTelegramProfile {
  telegramId: number;
  displayName: string;
  authDate: number;
  isDevUser?: boolean;
}

const MAX_AUTH_AGE_SECONDS = 86400; // 24 hours

/**
 * Validates Telegram initData string and returns the verified profile.
 * Throws an Error if validation fails.
 */
export async function verifyTelegramInitData(
  initData: string,
  botToken: string,
  allowDevAuth = false
): Promise<VerifiedTelegramProfile> {
  if (!initData || typeof initData !== 'string') {
    throw new Error('MISSING_INIT_DATA: Telegram initData is required');
  }

  // Optional Development / Test mode: only if explicitly enabled on server
  if (allowDevAuth && initData.startsWith('dev:')) {
    const parts = initData.substring(4).split(':');
    const devId = parseInt(parts[0], 10);
    const devName = parts[1] ? decodeURIComponent(parts[1]) : 'Dev Player';
    if (!isNaN(devId) && devId > 0) {
      return {
        telegramId: devId,
        displayName: devName,
        authDate: Math.floor(Date.now() / 1000),
        isDevUser: true,
      };
    }
  }

  if (!botToken) {
    throw new Error('SERVER_CONFIG_ERROR: TELEGRAM_BOT_TOKEN is not configured on server');
  }

  const urlParams = new URLSearchParams(initData);
  const hash = urlParams.get('hash');
  if (!hash) {
    throw new Error('INVALID_INIT_DATA: Missing hash in initData');
  }

  const authDateStr = urlParams.get('auth_date');
  if (!authDateStr) {
    throw new Error('INVALID_INIT_DATA: Missing auth_date');
  }

  const authDate = parseInt(authDateStr, 10);
  const currentTimestamp = Math.floor(Date.now() / 1000);
  if (isNaN(authDate) || currentTimestamp - authDate > MAX_AUTH_AGE_SECONDS || authDate > currentTimestamp + 300) {
    throw new Error('EXPIRED_AUTH_DATA: Authentication signature expired or invalid timestamp');
  }

  // Remove hash and sort remaining parameters alphabetically
  const dataCheckArr: string[] = [];
  const entries: [string, string][] = [];
  for (const [key, value] of urlParams.entries()) {
    if (key !== 'hash') {
      entries.push([key, value]);
    }
  }
  entries.sort(([a], [b]) => a.localeCompare(b));

  for (const [key, value] of entries) {
    dataCheckArr.push(`${key}=${value}`);
  }
  const dataCheckString = dataCheckArr.join('\n');

  // Step 1: secret_key = HMAC_SHA256("WebAppData", botToken)
  const encoder = new TextEncoder();
  const webAppDataKey = await crypto.subtle.importKey(
    'raw',
    encoder.encode('WebAppData'),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const secretKeyBytes = await crypto.subtle.sign('HMAC', webAppDataKey, encoder.encode(botToken));

  // Step 2: computed_hash = HMAC_SHA256(secret_key, dataCheckString)
  const secretKey = await crypto.subtle.importKey(
    'raw',
    secretKeyBytes,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const signatureBytes = await crypto.subtle.sign('HMAC', secretKey, encoder.encode(dataCheckString));

  // Convert to hex
  const signatureHex = Array.from(new Uint8Array(signatureBytes))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');

  if (signatureHex.toLowerCase() !== hash.toLowerCase()) {
    throw new Error('AUTHENTICATION_FAILED: Invalid signature in Telegram initData');
  }

  // Extract user JSON
  const userJson = urlParams.get('user');
  if (!userJson) {
    throw new Error('INVALID_INIT_DATA: Missing user data');
  }

  let user: TelegramUser;
  try {
    user = JSON.parse(userJson);
  } catch {
    throw new Error('MALFORMED_USER_DATA: Failed to parse user payload');
  }

  if (!user.id || typeof user.id !== 'number') {
    throw new Error('INVALID_USER_ID: Valid Telegram user ID is required');
  }

  const firstName = (user.first_name || '').trim();
  const lastName = (user.last_name || '').trim();
  const displayName = [firstName, lastName].filter(Boolean).join(' ') || `Player ${user.id}`;

  return {
    telegramId: user.id,
    displayName,
    authDate,
  };
}

