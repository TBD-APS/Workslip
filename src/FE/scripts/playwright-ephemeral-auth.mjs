const LOOPBACK_HOSTS = new Set(['127.0.0.1', 'localhost', '::1']);

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
  if (!LOOPBACK_HOSTS.has(url.hostname)) {
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
  fetchImpl = fetch,
}) {
  requireLoopbackOrigin(appUrl, 'WORKSLIP_PLAYWRIGHT_APP_URL');
  const session = await issueLocalDevelopmentToken({ apiUrl, email, fetchImpl });

  await context.addInitScript(({ token, userEmail }) => {
    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', userEmail);
  }, {
    token: session.token,
    userEmail: session.user.email,
  });

  return session;
}
