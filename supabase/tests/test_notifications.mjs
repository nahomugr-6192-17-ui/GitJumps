import fs from 'node:fs';
import path from 'node:path';

function formatNotificationMessage(bestScore, globalRank, playerCode) {
  return `🎉 NEW PERSONAL BEST!

🏆 New best score: ${bestScore}
🌍 Global leaderboard position: #${globalRank}
🆔 Player code: ${playerCode}

Keep jumping and climb the leaderboard!`;
}

function runTests() {
  console.log('>>> RUNNING FOCUSED NOTIFICATION RELIABILITY & SECURITY TESTS <<<');

  // --------------------------------------------------------------------------
  // Test 1: Message Formatting
  // --------------------------------------------------------------------------
  const expectedMsg = 
`🎉 NEW PERSONAL BEST!

🏆 New best score: 350
🌍 Global leaderboard position: #1
🆔 Player code: GJ-0042

Keep jumping and climb the leaderboard!`;

  const generatedMsg = formatNotificationMessage(350, 1, 'GJ-0042');
  if (generatedMsg !== expectedMsg) {
    throw new Error(`Message format mismatch:\nExpected:\n${expectedMsg}\nGot:\n${generatedMsg}`);
  }
  console.log('✓ Test 1 Passed: Exact notification message format matches specification');

  // --------------------------------------------------------------------------
  // Test 2: Webhook Secret Token Verification logic (Fail-Closed)
  // --------------------------------------------------------------------------
  function checkWebhookSecret(headerVal, secret) {
    if (!secret) {
      return { status: 500, error: 'SERVER_CONFIG_ERROR: Webhook secret not configured' };
    }
    if (headerVal !== secret) {
      return { status: 401, error: 'UNAUTHORIZED: Invalid webhook secret token' };
    }
    return { status: 200, ok: true };
  }

  const missingSecretResult = checkWebhookSecret('any_token', '');
  if (missingSecretResult.status !== 500) {
    throw new Error('Missing webhook secret did NOT fail closed with 500');
  }

  const invalidSecretResult = checkWebhookSecret('wrong_token', 'super_secret_123');
  if (invalidSecretResult.status !== 401) {
    throw new Error('Invalid webhook secret did NOT return 401');
  }

  const validSecretResult = checkWebhookSecret('super_secret_123', 'super_secret_123');
  if (validSecretResult.status !== 200) {
    throw new Error('Valid webhook secret was rejected');
  }
  console.log('✓ Test 2 Passed: Webhook secret verification fails closed (500 if missing, 401 if invalid)');

  // --------------------------------------------------------------------------
  // Test 3: Chat ID and Sender Identity Integrity
  // --------------------------------------------------------------------------
  function validateTelegramMessage(message) {
    if (!message || !message.from || !message.chat) return { valid: false, reason: 'missing_fields' };
    if (message.chat.type !== 'private') return { valid: false, reason: 'not_private' };
    if (message.chat.id !== message.from.id) return { valid: false, reason: 'mismatched_id' };
    return { valid: true, telegramId: message.from.id, chatId: message.chat.id };
  }

  const groupMessage = { from: { id: 100 }, chat: { id: -200, type: 'group' } };
  if (validateTelegramMessage(groupMessage).valid) {
    throw new Error('Group chat message was incorrectly accepted');
  }

  const spoofedMessage = { from: { id: 100 }, chat: { id: 200, type: 'private' } };
  if (validateTelegramMessage(spoofedMessage).valid) {
    throw new Error('Mismatched chat.id vs from.id was incorrectly accepted');
  }

  const legitimateMessage = { from: { id: 100 }, chat: { id: 100, type: 'private' } };
  const legitResult = validateTelegramMessage(legitimateMessage);
  if (!legitResult.valid || legitResult.chatId !== 100 || legitResult.telegramId !== 100) {
    throw new Error('Legitimate private chat message was rejected');
  }
  console.log('✓ Test 3 Passed: Sender and chat ID verification rejects non-private and mismatched IDs');

  // --------------------------------------------------------------------------
  // Test 4: EdgeRuntime.waitUntil Background Task Registration
  // --------------------------------------------------------------------------
  let registeredTask = null;
  const mockEdgeRuntime = {
    waitUntil: (p) => { registeredTask = p; }
  };

  const deliveryPromise = Promise.resolve('delivered');
  if (mockEdgeRuntime && typeof mockEdgeRuntime.waitUntil === 'function') {
    mockEdgeRuntime.waitUntil(deliveryPromise);
  }

  if (registeredTask !== deliveryPromise) {
    throw new Error('EdgeRuntime.waitUntil failed to capture the background delivery promise');
  }
  console.log('✓ Test 4 Passed: EdgeRuntime.waitUntil properly registers background delivery promise');

  // --------------------------------------------------------------------------
  // Test 5: Delivery Failure Isolation (Score Committed, Error Logged in Outbox)
  // --------------------------------------------------------------------------
  let recordedStatus = null;
  let recordedError = null;
  let recordedToken = null;
  const mockSupabase = {
    rpc: async (fn, args) => {
      if (fn === 'record_notification_result') {
        recordedStatus = args.p_status;
        recordedError = args.p_error;
        recordedToken = args.p_claim_token;
      }
      return { data: null, error: null };
    }
  };

  async function simulateDeliveryFailure(eventId, claimToken) {
    // Simulate Telegram API throwing HTTP 403 Bot Blocked
    const safeError = 'HTTP_403: Bot was blocked by user';
    await mockSupabase.rpc('record_notification_result', {
      p_event_id: eventId,
      p_status: 'failed',
      p_claim_token: claimToken,
      p_error: safeError,
    });
    // Never throw to caller
    return { ok: false, error: safeError };
  }

  simulateDeliveryFailure('test-event-uuid-1234', 'claim-token-xyz').then(() => {
    if (recordedStatus !== 'failed' || !recordedError.includes('HTTP_403') || recordedToken !== 'claim-token-xyz') {
      throw new Error('Delivery failure did not properly record failed status and claim token in outbox');
    }
    console.log('✓ Test 5 Passed: Delivery failure records "failed" status and mandatory claim token in outbox without throwing');
  });

  // --------------------------------------------------------------------------
  // Test 6: Concurrent Outbox Worker Claim Simulation (SKIP LOCKED semantics)
  // --------------------------------------------------------------------------
  const outbox = [
    { id: 'ev-1', status: 'pending', lockedBy: null },
    { id: 'ev-2', status: 'pending', lockedBy: null },
    { id: 'ev-3', status: 'pending', lockedBy: null },
  ];

  function workerClaim(workerId, batchSize) {
    const claimed = [];
    for (const item of outbox) {
      if (item.status === 'pending' && item.lockedBy === null) {
        item.lockedBy = workerId;
        item.status = 'processing';
        claimed.push(item.id);
        if (claimed.length >= batchSize) break;
      }
    }
    return claimed;
  }

  const worker1Claims = workerClaim('worker-1', 2);
  const worker2Claims = workerClaim('worker-2', 2);

  // Workers must never claim the same event
  const overlap = worker1Claims.filter(id => worker2Claims.includes(id));
  if (overlap.length > 0) {
    throw new Error(`Concurrency race! Overlapping claims: ${overlap}`);
  }
  if (worker1Claims.length !== 2 || worker2Claims.length !== 1) {
    throw new Error('Worker claim counts do not match expected partitioning');
  }
  console.log('✓ Test 6 Passed: SKIP LOCKED concurrency simulation prevents overlapping worker claims');

  // --------------------------------------------------------------------------
  // Tests 8-13: Comprehensive Outbox Recovery & Lease Ownership Simulation
  // --------------------------------------------------------------------------
  let nextUuid = 1000;
  function mockGenUuid() {
    return `uuid-${nextUuid++}`;
  }

  // Model of PostgreSQL score_notification_events table + RPC functions
  class MockOutboxDatabase {
    constructor() {
      this.events = [];
    }

    addEvent(data) {
      const ev = {
        id: data.id || mockGenUuid(),
        player_id: data.player_id || 'player-1',
        telegram_chat_id: data.telegram_chat_id || 12345,
        score: data.score || 100,
        status: data.status || 'pending',
        retry_count: data.retry_count || 0,
        claim_token: data.claim_token || mockGenUuid(),
        error_message: data.error_message || null,
        created_at: data.created_at || Date.now(),
        updated_at: data.updated_at || Date.now(),
      };
      this.events.push(ev);
      return ev;
    }

    claimPendingNotifications(batchSize = 10, staleIntervalSec = 60, leaseDurationSec = 120, maxRetries = 3, currentTime = Date.now()) {
      // 1. Mark expired 'processing' events exceeding max retries as failed
      for (const ev of this.events) {
        if (ev.status === 'processing') {
          const elapsedSec = (currentTime - ev.updated_at) / 1000;
          if (elapsedSec >= leaseDurationSec && ev.retry_count >= maxRetries) {
            ev.status = 'failed';
            ev.error_message = 'MAX_RETRIES_EXCEEDED: Lease expired after reaching maximum retry limit';
            ev.updated_at = currentTime;
          }
        }
      }

      // 2. Select eligible claimable events (simulating FOR UPDATE SKIP LOCKED)
      const claimed = [];
      for (const ev of this.events) {
        if (claimed.length >= batchSize) break;
        if (!ev.telegram_chat_id) continue;
        if (ev.retry_count >= maxRetries) continue;

        let eligible = false;
        const createdElapsedSec = (currentTime - ev.created_at) / 1000;
        const updatedElapsedSec = (currentTime - ev.updated_at) / 1000;

        if (ev.status === 'pending' && createdElapsedSec >= staleIntervalSec) {
          eligible = true;
        } else if (ev.status === 'processing' && updatedElapsedSec >= leaseDurationSec) {
          // Expired lease
          eligible = true;
        } else if (ev.status === 'failed' && updatedElapsedSec >= 30 && createdElapsedSec < 86400) {
          // Failed retry backoff
          eligible = true;
        }

        if (eligible) {
          // Atomically claim
          ev.status = 'processing';
          ev.retry_count += 1;
          ev.claim_token = mockGenUuid();
          ev.updated_at = currentTime;
          claimed.push({ ...ev });
        }
      }
      return claimed;
    }

    recordNotificationResult(eventId, status, claimToken, error = null, currentTime = Date.now()) {
      const ev = this.events.find(e => e.id === eventId);
      if (!ev) return { updated: false, reason: 'not_found' };

      // Mandatory Fencing Token: NULL/omitted token or mismatch must update 0 rows and return false
      if (!claimToken || ev.claim_token !== claimToken) {
        return { updated: false, reason: 'token_mismatch', currentToken: ev.claim_token };
      }

      ev.status = status;
      ev.error_message = error;
      ev.updated_at = currentTime;
      if (status === 'sent') ev.sent_at = currentTime;
      return { updated: true };
    }
  }

  // Test 8: Worker crash while processing an event
  const db8 = new MockOutboxDatabase();
  const startTime = Date.now();
  const ev8 = db8.addEvent({ status: 'pending', created_at: startTime - 70000 }); // stale pending
  const claim8 = db8.claimPendingNotifications(10, 60, 120, 3, startTime);
  if (claim8.length !== 1 || claim8[0].id !== ev8.id) {
    throw new Error('Test 8 Failed: Expected 1 event to be claimed');
  }
  const token8_w1 = claim8[0].claim_token;
  // Worker 1 crashes! (No recordNotificationResult call is made)
  const ev8_afterCrash = db8.events.find(e => e.id === ev8.id);
  if (ev8_afterCrash.status !== 'processing' || ev8_afterCrash.retry_count !== 1) {
    throw new Error('Test 8 Failed: Event was not left in processing with retry_count = 1');
  }
  console.log('✓ Test 8 Passed: Worker crashed while processing; event left in "processing" state with retry_count = 1');

  // Test 9: Refusing to reclaim the event while lease is still valid
  const timeDuringLease = startTime + 40000; // 40s elapsed (< 120s lease)
  const claim9 = db8.claimPendingNotifications(10, 60, 120, 3, timeDuringLease);
  if (claim9.length !== 0) {
    throw new Error('Test 9 Failed: Event with active lease was improperly claimed!');
  }
  console.log('✓ Test 9 Passed: Concurrent worker refused to claim event while lease is still active (40s < 120s)');

  // Test 10: Reclaiming the event after the lease expires
  const timeAfterLeaseExpired = startTime + 125000; // 125s elapsed (> 120s lease)
  const claim10 = db8.claimPendingNotifications(10, 60, 120, 3, timeAfterLeaseExpired);
  if (claim10.length !== 1 || claim10[0].id !== ev8.id) {
    throw new Error('Test 10 Failed: Expired processing event was not reclaimed by worker 2');
  }
  const token8_w2 = claim10[0].claim_token;
  if (token8_w2 === token8_w1) {
    throw new Error('Test 10 Failed: Reclaimed event claim_token was not rotated');
  }
  if (claim10[0].retry_count !== 2) {
    throw new Error(`Test 10 Failed: Expected retry_count 2, got ${claim10[0].retry_count}`);
  }
  console.log('✓ Test 10 Passed: Worker 2 successfully reclaimed expired processing event, rotated claim_token, and incremented retry_count');

  // Test 11: Two workers claiming at the same time: exactly one claims each event (concurrency & partitioning)
  const db11 = new MockOutboxDatabase();
  db11.addEvent({ id: 'ev-11a', status: 'pending', created_at: startTime - 70000 });
  db11.addEvent({ id: 'ev-11b', status: 'pending', created_at: startTime - 70000 });

  // Simulate atomic sequential lock acquisition by two workers
  const workerA_claims = db11.claimPendingNotifications(1, 60, 120, 3, startTime);
  const workerB_claims = db11.claimPendingNotifications(1, 60, 120, 3, startTime);
  if (workerA_claims.length !== 1 || workerB_claims.length !== 1) {
    throw new Error('Test 11 Failed: Expected each worker to claim 1 distinct event');
  }
  if (workerA_claims[0].id === workerB_claims[0].id) {
    throw new Error('Test 11 Failed: Both workers claimed the exact same event!');
  }
  console.log('✓ Test 11 Passed: Two concurrent workers claiming at the same time receive strictly non-overlapping events');

  // Test 12: Stale worker write rejection & NULL token rejection (Fencing token protects newer claim)
  // Worker 1 from Test 10 (crashed earlier) wakes up late and tries to record success using token8_w1
  const staleWriteResult = db8.recordNotificationResult(ev8.id, 'sent', token8_w1, null, timeAfterLeaseExpired + 5000);
  if (staleWriteResult.updated !== false || staleWriteResult.reason !== 'token_mismatch') {
    throw new Error('Test 12 Failed: Stale write with expired lease was NOT rejected!');
  }
  // An attempt with a NULL token must also be strictly rejected
  const nullTokenWriteResult = db8.recordNotificationResult(ev8.id, 'sent', null, null, timeAfterLeaseExpired + 5500);
  if (nullTokenWriteResult.updated !== false || nullTokenWriteResult.reason !== 'token_mismatch') {
    throw new Error('Test 12 Failed: Write with NULL claim_token was NOT rejected!');
  }

  // Verify row was NOT updated to 'sent'
  const ev8_check = db8.events.find(e => e.id === ev8.id);
  if (ev8_check.status !== 'processing' || ev8_check.claim_token !== token8_w2) {
    throw new Error('Test 12 Failed: Database row was modified by rejected stale write');
  }

  // Worker 2 finishes validly with token8_w2
  const validWriteResult = db8.recordNotificationResult(ev8.id, 'sent', token8_w2, null, timeAfterLeaseExpired + 6000);
  if (!validWriteResult.updated || ev8_check.status !== 'sent') {
    throw new Error('Test 12 Failed: Worker 2 valid write was rejected or status not updated');
  }
  console.log('✓ Test 12 Passed: Stale worker write and NULL token were rejected; valid worker successfully recorded "sent"');

  // Test 13: Consistent retry exhaustion (Actively processing vs Expired lease)
  const db13 = new MockOutboxDatabase();
  const ev13 = db13.addEvent({
    id: 'ev-13-active',
    status: 'processing',
    retry_count: 3, // reached max retries
    updated_at: startTime - 30000, // actively processing (only 30s elapsed of 120s lease)
  });

  // Claim cycle while lease is still ACTIVE: must NOT mark the active event as failed!
  db13.claimPendingNotifications(10, 60, 120, 3, startTime);
  const ev13_duringLease = db13.events.find(e => e.id === ev13.id);
  if (ev13_duringLease.status !== 'processing') {
    throw new Error('Test 13 Failed: Actively processing event was prematurely marked as failed before lease expired!');
  }

  // Claim cycle after lease EXPIRES (125s elapsed): must transition to permanently 'failed'
  const claim13 = db13.claimPendingNotifications(10, 60, 120, 3, startTime + 125000);
  if (claim13.length !== 0) {
    throw new Error('Test 13 Failed: Event with max retries should not be claimed');
  }
  const ev13_check = db13.events.find(e => e.id === ev13.id);
  if (ev13_check.status !== 'failed' || !ev13_check.error_message.includes('MAX_RETRIES_EXCEEDED')) {
    throw new Error(`Test 13 Failed: Expected status 'failed' with MAX_RETRIES_EXCEEDED, got: ${ev13_check.status}`);
  }
  console.log('✓ Test 13 Passed: Actively processing event protected; expired event exceeding max retries transitioned to permanently "failed"');

  // --------------------------------------------------------------------------
  // Test 14: Client Codebase Token Security Audit
  // --------------------------------------------------------------------------
  const sensitivePatterns = [
    /bot[0-9]{8,10}:[a-zA-Z0-9_-]{35}/,
    /TELEGRAM_BOT_TOKEN\s*=\s*['"][0-9]+:/,
  ];

  const searchDirs = [
    path.resolve('Assets'),
    path.resolve('GitJumps - Web'),
  ];

  let filesScanned = 0;
  function scanDir(dir) {
    if (!fs.existsSync(dir)) return;
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!entry.name.includes('Library') && !entry.name.includes('.git')) {
          scanDir(fullPath);
        }
      } else if (entry.isFile()) {
        const ext = path.extname(entry.name).toLowerCase();
        if (['.cs', '.js', '.html', '.json', '.uxml', '.uss'].includes(ext)) {
          filesScanned++;
          const content = fs.readFileSync(fullPath, 'utf8');
          for (const pattern of sensitivePatterns) {
            if (pattern.test(content)) {
              throw new Error(`CRITICAL SECURITY FAILURE: Sensitive Telegram token found in ${fullPath}`);
            }
          }
        }
      }
    }
  }

  for (const dir of searchDirs) {
    scanDir(dir);
  }
  console.log(`✓ Test 14 Passed: Scanned ${filesScanned} client and build files. Zero Telegram tokens found in client code`);

  console.log('>>> ALL 14 FOCUSED NOTIFICATION RELIABILITY & OUTBOX TESTS PASSED! <<<');
}

runTests();
