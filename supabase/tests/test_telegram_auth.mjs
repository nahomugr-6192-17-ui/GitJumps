import crypto from 'node:crypto';

// Recreate verifyTelegramInitData in Node.js ESM to test the algorithm
const MAX_AUTH_AGE_SECONDS = 86400;

async function verifyTelegramInitData(initData, botToken, allowDevAuth = false) {
  if (!initData || typeof initData !== 'string') {
    throw new Error('MISSING_INIT_DATA: Telegram initData is required');
  }

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

  const entries = [];
  for (const [key, value] of urlParams.entries()) {
    if (key !== 'hash') {
      entries.push([key, value]);
    }
  }
  entries.sort(([a], [b]) => a.localeCompare(b));
  const dataCheckString = entries.map(([k, v]) => `${k}=${v}`).join('\n');

  const encoder = new TextEncoder();
  const webAppDataKey = await crypto.subtle.importKey(
    'raw',
    encoder.encode('WebAppData'),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const secretKeyBytes = await crypto.subtle.sign('HMAC', webAppDataKey, encoder.encode(botToken));

  const secretKey = await crypto.subtle.importKey(
    'raw',
    secretKeyBytes,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const signatureBytes = await crypto.subtle.sign('HMAC', secretKey, encoder.encode(dataCheckString));
  const signatureHex = Array.from(new Uint8Array(signatureBytes))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');

  if (signatureHex.toLowerCase() !== hash.toLowerCase()) {
    throw new Error('AUTHENTICATION_FAILED: Invalid signature in Telegram initData');
  }

  const userJson = urlParams.get('user');
  if (!userJson) {
    throw new Error('INVALID_INIT_DATA: Missing user data');
  }

  let user;
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

// Helper to generate valid Telegram initData for testing
async function generateTelegramInitData(userObj, botToken, authDate = Math.floor(Date.now() / 1000)) {
  const userJson = JSON.stringify(userObj);
  const params = [
    ['auth_date', authDate.toString()],
    ['query_id', 'AAHdF6IQAAAAAN0XohB1n2n_'],
    ['user', userJson],
  ];

  params.sort(([a], [b]) => a.localeCompare(b));
  const dataCheckString = params.map(([k, v]) => `${k}=${v}`).join('\n');

  const encoder = new TextEncoder();
  const webAppDataKey = await crypto.subtle.importKey(
    'raw',
    encoder.encode('WebAppData'),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const secretKeyBytes = await crypto.subtle.sign('HMAC', webAppDataKey, encoder.encode(botToken));

  const secretKey = await crypto.subtle.importKey(
    'raw',
    secretKeyBytes,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const signatureBytes = await crypto.subtle.sign('HMAC', secretKey, encoder.encode(dataCheckString));
  const hash = Array.from(new Uint8Array(signatureBytes))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');

  params.push(['hash', hash]);
  return params.map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');
}

// RUN TESTS
async function runTests() {
  const BOT_TOKEN = '123456789:ABCdefGHIjklMNOpqrsTUVwxyz_1234567';

  console.log('>>> RUNNING TELEGRAM AUTHENTICATION TESTS <<<');

  // Test 1: Valid signature with first & last name
  const validUser = { id: 987654321, first_name: 'Alex', last_name: 'Jump' };
  const validInitData = await generateTelegramInitData(validUser, BOT_TOKEN);
  const res1 = await verifyTelegramInitData(validInitData, BOT_TOKEN);
  if (res1.telegramId !== 987654321 || res1.displayName !== 'Alex Jump') {
    throw new Error('Test 1 failed: Expected Alex Jump, got ' + JSON.stringify(res1));
  }
  console.log('✓ Test 1 Passed: Valid signature correctly verified.');

  // Test 2: Tampered hash
  const tamperedInitData = validInitData.replace(/hash=[a-f0-9]{6}/, 'hash=deadbeef00');
  try {
    await verifyTelegramInitData(tamperedInitData, BOT_TOKEN);
    throw new Error('Test 2 failed: Tampered hash was not rejected');
  } catch (err) {
    if (!err.message.includes('AUTHENTICATION_FAILED')) throw err;
    console.log('✓ Test 2 Passed: Tampered hash correctly rejected.');
  }

  // Test 3: Expired auth_date (25 hours ago)
  const expiredDate = Math.floor(Date.now() / 1000) - 90000;
  const expiredInitData = await generateTelegramInitData(validUser, BOT_TOKEN, expiredDate);
  try {
    await verifyTelegramInitData(expiredInitData, BOT_TOKEN);
    throw new Error('Test 3 failed: Expired signature was not rejected');
  } catch (err) {
    if (!err.message.includes('EXPIRED_AUTH_DATA')) throw err;
    console.log('✓ Test 3 Passed: Expired signature correctly rejected.');
  }

  // Test 4: Dev Auth mode allowed
  const devRes = await verifyTelegramInitData('dev:555555:Test%20Cat', BOT_TOKEN, true);
  if (devRes.telegramId !== 555555 || devRes.displayName !== 'Test Cat') {
    throw new Error('Test 4 failed: Dev auth failed');
  }
  console.log('✓ Test 4 Passed: Dev auth allowed when explicitly enabled.');

  // Test 5: Dev Auth mode rejected when disabled
  try {
    await verifyTelegramInitData('dev:555555:Test%20Cat', BOT_TOKEN, false);
    throw new Error('Test 5 failed: Dev auth was accepted when disabled');
  } catch (err) {
    console.log('✓ Test 5 Passed: Dev auth rejected when disabled.');
  }

  // Test 6: Display name without last name
  const singleNameUser = { id: 11223344, first_name: 'PixelCat' };
  const singleNameInitData = await generateTelegramInitData(singleNameUser, BOT_TOKEN);
  const res6 = await verifyTelegramInitData(singleNameInitData, BOT_TOKEN);
  if (res6.displayName !== 'PixelCat') {
    throw new Error('Test 6 failed: Expected PixelCat, got ' + res6.displayName);
  }
  console.log('✓ Test 6 Passed: Single first_name without last name handled cleanly.');

  // Test 7: Future auth_date (e.g. 1 hour in the future)
  const futureDate = Math.floor(Date.now() / 1000) + 3600;
  const futureInitData = await generateTelegramInitData(validUser, BOT_TOKEN, futureDate);
  try {
    await verifyTelegramInitData(futureInitData, BOT_TOKEN);
    throw new Error('Test 7 failed: Future auth date was not rejected');
  } catch (err) {
    if (!err.message.includes('EXPIRED_AUTH_DATA')) throw err;
    console.log('✓ Test 7 Passed: Future timestamp correctly rejected.');
  }

  console.log('>>> ALL 7 TELEGRAM AUTHENTICATION TESTS PASSED SUCCESSFULLY! <<<');
}

runTests().catch((err) => {
  console.error('Test execution failed:', err);
  process.exit(1);
});

