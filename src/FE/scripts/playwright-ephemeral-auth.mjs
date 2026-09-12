const LOOPBACK_HOSTS = new Set(['127.0.0.1', 'localhost', '::1']);

function normalizedHostname(url) {
  return url.hostname.startsWith('[') && url.hostname.endsWith(']')
    ? url.hostname.slice(1, -1)
    : url.hostname;
}

export function requireLoopbackOrigin(value, label = 'URL') {
  let url;
  try {
    url = new URL(String(value ?? '').trim());
  } catch {
    throw new Error(`${label} must be a valid HTTP(S) URL.`);
  }

  if (!['http:', 'https:'].includes(url.protocol)) {
    throw new Error(`${label} must use HTTP(S).`);
  }
  const hostname = normalizedHostname(url);
  if (!LOOPBACK_HOSTS.has(hostname)) {
    throw new Error(`${label} must target loopback; got ${url.hostname}.`);
  }
  if (url.username || url.password || url.search || url.hash) {
    throw new Error(`${label} must not include credentials, query, or fragment.`);
  }
  if (url.pathname !== '/' && url.pathname !== '') {
    throw new Error(`${label} must be an origin without a path.`);
  }

  return url.origin;
}

export async function issueLocalDevelopmentToken({ apiUrl, email, fetchImpl = fetch }) {
  const apiOrigin = requireLoopbackOrigin(apiUrl, 'WORKSLIP_PLAYWRIGHT_API_URL');
  const normalizedEmail = String(email ?? '').trim().toLowerCase();
  if (!normalizedEmail) throw new Error('Synthetic development auth requires an email.');

  const response = await fetchImpl(`${apiOrigin}/api/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email: normalizedEmail }),
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok || typeof payload?.token !== 'string' || !payload.token || !payload?.user?.email) {
    throw new Error(`Development token request failed with HTTP ${response.status}.`);
  }

  return { token: payload.token, user: payload.user, apiOrigin };
}

export async function seedLocalBrowserSession(context, {
  appUrl,
  apiUrl,
  email,
}) {
  const appOrigin = requireLoopbackOrigin(appUrl, 'WORKSLIP_PLAYWRIGHT_APP_URL');
  const apiOrigin = requireLoopbackOrigin(apiUrl, 'WORKSLIP_PLAYWRIGHT_API_URL');
  const normalizedEmail = String(email ?? '').trim().toLowerCase();
  const response = await context.request.post(`${apiOrigin}/api/dev/token`, {
    headers: { Origin: appOrigin, Accept: 'application/json' },
    data: { email: normalizedEmail },
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok() || typeof payload?.token !== 'string' || !payload.token || !payload?.user?.email) {
    throw new Error(`Development browser session request failed with HTTP ${response.status()}.`);
  }
  const session = { token: payload.token, user: payload.user, apiOrigin };

  await context.addInitScript(({ token, userEmail }) => {
    if (sessionStorage.getItem('__workslip_playwright_auth_seeded') === '1') return;
    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', userEmail);
    sessionStorage.setItem('__workslip_playwright_auth_seeded', '1');
  }, {
    token: session.token,
    userEmail: session.user.email,
  });

  return session;
}
