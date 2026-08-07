import process from 'node:process';

const apiBaseUrl = requireEnv('WORKSLIP_API_URL').replace(/\/+$/, '');
const bootstrapToken = requireEnv('WORKSLIP_BOOTSTRAP_SUPERADMIN_TOKEN');
const serverId = requireEnv('MAILOSAUR_SERVER_ID');
const allowProduction = process.env.WORKSLIP_ALLOW_PRODUCTION_SYNTHETIC_BOOTSTRAP === 'true';

if (/mrsoftware\.dk|prod/i.test(apiBaseUrl) && !allowProduction) {
  throw new Error(
    'Refusing to bootstrap synthetic identities against a production-looking target. ' +
    'Set WORKSLIP_ALLOW_PRODUCTION_SYNTHETIC_BOOTSTRAP=true only for an explicit one-time production test-tenant setup.',
  );
}

const actor = await api('/api/auth/me');
if (String(actor?.role ?? '').toLowerCase() !== 'superadmin') {
  throw new Error('Synthetic identity bootstrap requires a normally authenticated Superadmin token.');
}

const identities = [
  identity('User', 'Synthetic Test User', '10000101'),
  identity('Auditor', 'Synthetic Test Auditor', '10000102'),
  identity('Admin', 'Synthetic Test Admin', '10000103'),
  identity('Superadmin', 'Synthetic Test Superadmin', '10000104'),
];

const existingResponse = await api('/api/users/?limit=200&offset=0');
const existingUsers = Array.isArray(existingResponse?.items)
  ? existingResponse.items
  : Array.isArray(existingResponse?.users)
    ? existingResponse.users
    : Array.isArray(existingResponse)
      ? existingResponse
      : [];

for (const expected of identities) {
  const existing = existingUsers.find((user) =>
    String(user?.email ?? '').toLowerCase() === expected.email.toLowerCase());

  if (existing) {
    if (String(existing.role).toLowerCase() !== expected.role.toLowerCase()) {
      throw new Error(
        `Synthetic identity ${expected.email} exists with role ${existing.role}; expected ${expected.role}.`,
      );
    }
    console.log(`exists ${expected.role}: ${expected.email}`);
    continue;
  }

  const created = await api('/api/users/', {
    method: 'POST',
    body: expected,
  });
  if (!created?.id) throw new Error(`Synthetic ${expected.role} was not created.`);
  console.log(`created ${expected.role}: ${expected.email}`);
}

function identity(role, displayName, phone) {
  const prefix = role.toLowerCase();
  const override = process.env[`WORKSLIP_SYNTHETIC_${role.toUpperCase()}_EMAIL`]?.trim();
  return {
    email: override || `${prefix}@${serverId}.mailosaur.net`,
    displayName,
    phone,
    role,
  };
}

async function api(pathname, options = {}) {
  const response = await fetch(`${apiBaseUrl}${pathname}`, {
    method: options.method ?? 'GET',
    headers: {
      Authorization: `Bearer ${bootstrapToken}`,
      Accept: 'application/json',
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: options.body ? JSON.stringify(options.body) : undefined,
  });

  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(`${options.method ?? 'GET'} ${pathname} returned HTTP ${response.status}.`);
  }
  return payload;
}

function requireEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required.`);
  return value;
}
