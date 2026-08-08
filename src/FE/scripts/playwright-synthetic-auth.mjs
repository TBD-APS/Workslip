import process from 'node:process';

const GRAPH_BASE = 'https://graph.microsoft.com/v1.0';
const GRAPH_SCOPE = 'https://graph.microsoft.com/.default';
const GITHUB_OIDC_AUDIENCE = 'api://AzureADTokenExchange';
const DEFAULT_WAIT_MS = 30_000;
const POLL_INTERVAL_MS = 1_000;
const ROLE_ENV_NAMES = {
  User: 'WORKSLIP_SYNTHETIC_USER_EMAIL',
  Auditor: 'WORKSLIP_SYNTHETIC_AUDITOR_EMAIL',
  Admin: 'WORKSLIP_SYNTHETIC_ADMIN_EMAIL',
  Superadmin: 'WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL',
};

let cachedAccessToken = null;
let cachedAccessTokenExpiresAt = 0;

export function createSyntheticInbox({ timeoutMs = DEFAULT_WAIT_MS } = {}) {
  const mailbox = requireEnv('WORKSLIP_SYNTHETIC_MAILBOX').toLowerCase();

  return {
    emailForRole(role) {
      const envName = ROLE_ENV_NAMES[role];
      if (!envName) throw new Error(`Unsupported synthetic role: ${role}`);
      return requireEnv(envName).toLowerCase();
    },

    async waitForCode(email, requestedAt = new Date()) {
      const normalizedEmail = String(email ?? '').trim().toLowerCase();
      if (!normalizedEmail) throw new Error('Synthetic authentication requires an email address.');
      return waitForCode({
        email: normalizedEmail,
        requestedAt,
        mailbox,
        timeoutMs,
      });
    },
  };
}

async function waitForCode({ email, requestedAt, mailbox, timeoutMs }) {
  const deadline = Date.now() + timeoutMs;
  const receivedAfter = new Date(requestedAt.getTime() - 2_000).toISOString();
  const checkedMessageIds = new Set();

  while (Date.now() < deadline) {
    const accessToken = await graphAccessToken(timeoutMs);
    const listUrl = new URL(`${GRAPH_BASE}/users/${encodeURIComponent(mailbox)}/mailFolders/inbox/messages`);
    listUrl.searchParams.set('$select', 'id,receivedDateTime');
    listUrl.searchParams.set('$filter', `receivedDateTime ge ${receivedAfter}`);
    listUrl.searchParams.set('$orderby', 'receivedDateTime desc');
    listUrl.searchParams.set('$top', '25');

    const listResponse = await fetch(listUrl, {
      headers: graphHeaders(accessToken),
      signal: AbortSignal.timeout(Math.min(10_000, timeoutMs)),
    });
    if (!listResponse.ok) {
      throw new Error(`Microsoft Graph inbox lookup returned HTTP ${listResponse.status}.`);
    }

    const listResult = await listResponse.json();
    for (const candidate of listResult?.value ?? []) {
      if (!candidate?.id || checkedMessageIds.has(candidate.id)) continue;
      checkedMessageIds.add(candidate.id);

      const message = await readMessage(mailbox, candidate.id, accessToken, timeoutMs);
      if (!messageTargetsEmail(message, email)) continue;

      const code = readCode(message);
      if (code) return code;
    }

    await delay(POLL_INTERVAL_MS);
  }

  throw new Error(`Timed out waiting for the one-time code for synthetic identity ${email}.`);
}

async function readMessage(mailbox, messageId, accessToken, timeoutMs) {
  const messageUrl = new URL(`${GRAPH_BASE}/users/${encodeURIComponent(mailbox)}/messages/${encodeURIComponent(messageId)}`);
  messageUrl.searchParams.set(
    '$select',
    'subject,body,bodyPreview,toRecipients,internetMessageHeaders,receivedDateTime',
  );

  const response = await fetch(messageUrl, {
    headers: graphHeaders(accessToken),
    signal: AbortSignal.timeout(Math.min(10_000, timeoutMs)),
  });
  if (!response.ok) throw new Error(`Microsoft Graph message lookup returned HTTP ${response.status}.`);
  return response.json();
}

function messageTargetsEmail(message, email) {
  const target = email.toLowerCase();
  const recipients = (message?.toRecipients ?? [])
    .map((entry) => entry?.emailAddress?.address?.toLowerCase())
    .filter(Boolean);
  if (recipients.includes(target)) return true;

  for (const header of message?.internetMessageHeaders ?? []) {
    const name = String(header?.name ?? '').toLowerCase();
    if (!['to', 'delivered-to', 'x-original-to', 'envelope-to'].includes(name)) continue;
    if (String(header?.value ?? '').toLowerCase().includes(target)) return true;
  }

  return false;
}

function readCode(message) {
  const content = [message?.subject, message?.bodyPreview, message?.body?.content]
    .filter(Boolean)
    .join('\n');
  const labelled = content.match(/(?:kode|code)[^0-9]{0,30}([0-9]{6})/i);
  if (labelled) return labelled[1];
  return content.match(/\b([0-9]{6})\b/)?.[1] ?? null;
}

async function graphAccessToken(timeoutMs) {
  const suppliedToken = process.env.WORKSLIP_GRAPH_ACCESS_TOKEN?.trim();
  if (suppliedToken) return suppliedToken;

  if (cachedAccessToken && Date.now() < cachedAccessTokenExpiresAt - 60_000) {
    return cachedAccessToken;
  }

  const tenantId = requireEnv('WORKSLIP_GRAPH_TENANT_ID');
  const clientId = requireEnv('WORKSLIP_GRAPH_CLIENT_ID');
  const githubAssertion = await githubOidcAssertion(timeoutMs);
  const tokenUrl = `https://login.microsoftonline.com/${encodeURIComponent(tenantId)}/oauth2/v2.0/token`;
  const body = new URLSearchParams({
    client_id: clientId,
    scope: GRAPH_SCOPE,
    grant_type: 'client_credentials',
    client_assertion_type: 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer',
    client_assertion: githubAssertion,
  });

  const response = await fetch(tokenUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
    body,
    signal: AbortSignal.timeout(Math.min(10_000, timeoutMs)),
  });
  if (!response.ok) throw new Error(`Microsoft Entra token exchange returned HTTP ${response.status}.`);

  const token = await response.json();
  if (!token?.access_token) throw new Error('Microsoft Entra token exchange did not return an access token.');
  cachedAccessToken = token.access_token;
  cachedAccessTokenExpiresAt = Date.now() + (Number(token.expires_in ?? 300) * 1_000);
  return cachedAccessToken;
}

async function githubOidcAssertion(timeoutMs) {
  const requestUrl = requireEnv('ACTIONS_ID_TOKEN_REQUEST_URL');
  const requestToken = requireEnv('ACTIONS_ID_TOKEN_REQUEST_TOKEN');
  const url = new URL(requestUrl);
  url.searchParams.set('audience', GITHUB_OIDC_AUDIENCE);

  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${requestToken}`, Accept: 'application/json' },
    signal: AbortSignal.timeout(Math.min(10_000, timeoutMs)),
  });
  if (!response.ok) throw new Error(`GitHub OIDC token request returned HTTP ${response.status}.`);

  const payload = await response.json();
  if (!payload?.value) throw new Error('GitHub OIDC token request did not return a token.');
  return payload.value;
}

function graphHeaders(accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Accept: 'application/json',
    Prefer: 'outlook.body-content-type="text"',
  };
}

function requireEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required for authenticated Playwright scenarios.`);
  return value;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
