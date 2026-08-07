import process from 'node:process';

const MAILOSAUR_API_BASE = 'https://mailosaur.com/api';
const DEFAULT_WAIT_MS = 30_000;
const POLL_INTERVAL_MS = 1_000;
const ROLE_PREFIXES = {
  User: 'user',
  Auditor: 'auditor',
  Admin: 'admin',
  Superadmin: 'superadmin',
};

export function createSyntheticAuth({ apiBaseUrl, apiTimeout }) {
  const baseUrl = apiBaseUrl.replace(/\/+$/, '');
  const mailosaurApiKey = requireEnv('MAILOSAUR_API_KEY');
  const mailosaurServerId = requireEnv('MAILOSAUR_SERVER_ID');

  return {
    emailForRole(role) {
      const prefix = ROLE_PREFIXES[role];
      if (!prefix) throw new Error(`Unsupported synthetic role: ${role}`);
      const override = process.env[`WORKSLIP_SYNTHETIC_${role.toUpperCase()}_EMAIL`];
      return override?.trim() || `${prefix}@${mailosaurServerId}.mailosaur.net`;
    },

    async authenticateEmail(email) {
      const normalizedEmail = String(email ?? '').trim().toLowerCase();
      if (!normalizedEmail) throw new Error('Synthetic authentication requires an email address.');
      assertMailosaurAddress(normalizedEmail, mailosaurServerId);

      const requestedAt = new Date();
      const sendResponse = await fetch(`${baseUrl}/api/auth/send-code`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ email: normalizedEmail }),
        signal: AbortSignal.timeout(apiTimeout),
      });
      if (!sendResponse.ok) {
        throw new Error(`Synthetic OTC request returned HTTP ${sendResponse.status}.`);
      }

      const code = await waitForCode({
        email: normalizedEmail,
        requestedAt,
        serverId: mailosaurServerId,
        apiKey: mailosaurApiKey,
        timeoutMs: Math.max(apiTimeout, DEFAULT_WAIT_MS),
      });

      const verifyResponse = await fetch(`${baseUrl}/api/auth/verify-code/${encodeURIComponent(code)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ email: normalizedEmail }),
        signal: AbortSignal.timeout(apiTimeout),
      });
      const payload = await verifyResponse.json().catch(() => null);
      if (!verifyResponse.ok || !payload?.token || !payload?.user) {
        throw new Error(`Synthetic OTC verification returned HTTP ${verifyResponse.status}.`);
      }

      return payload;
    },
  };
}

async function waitForCode({ email, requestedAt, serverId, apiKey, timeoutMs }) {
  const deadline = Date.now() + timeoutMs;
  const receivedAfter = new Date(requestedAt.getTime() - 2_000).toISOString();

  while (Date.now() < deadline) {
    const searchUrl = new URL(`${MAILOSAUR_API_BASE}/messages/search`);
    searchUrl.searchParams.set('server', serverId);
    searchUrl.searchParams.set('receivedAfter', receivedAfter);

    const searchResponse = await fetch(searchUrl, {
      method: 'POST',
      headers: {
        Authorization: basicAuth(apiKey),
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: JSON.stringify({ sentTo: email }),
      signal: AbortSignal.timeout(Math.min(10_000, timeoutMs)),
    });

    if (!searchResponse.ok) {
      throw new Error(`Mailosaur search returned HTTP ${searchResponse.status}.`);
    }

    const searchResult = await searchResponse.json();
    const messageId = searchResult?.items?.[0]?.id;
    if (messageId) {
      const code = await readCode(messageId, apiKey, timeoutMs);
      if (code) return code;
    }

    await delay(POLL_INTERVAL_MS);
  }

  throw new Error(`Timed out waiting for the one-time code for synthetic identity ${email}.`);
}

async function readCode(messageId, apiKey, timeoutMs) {
  const response = await fetch(`${MAILOSAUR_API_BASE}/messages/${encodeURIComponent(messageId)}`, {
    headers: { Authorization: basicAuth(apiKey), Accept: 'application/json' },
    signal: AbortSignal.timeout(Math.min(10_000, timeoutMs)),
  });
  if (!response.ok) throw new Error(`Mailosaur message lookup returned HTTP ${response.status}.`);

  const message = await response.json();
  const content = [
    message?.subject,
    message?.text?.body,
    message?.html?.body,
  ].filter(Boolean).join('\n');

  const labelled = content.match(/(?:kode|code)[^0-9]{0,30}([0-9]{6})/i);
  if (labelled) return labelled[1];

  return content.match(/\b([0-9]{6})\b/)?.[1] ?? null;
}

function basicAuth(apiKey) {
  return `Basic ${Buffer.from(`api:${apiKey}`).toString('base64')}`;
}

function assertMailosaurAddress(email, serverId) {
  const suffix = `@${serverId}.mailosaur.net`;
  if (!email.endsWith(suffix)) {
    throw new Error(`Synthetic identity must use the configured Mailosaur server (${suffix}).`);
  }
}

function requireEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required for authenticated Playwright scenarios.`);
  return value;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
