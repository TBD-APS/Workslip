from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[3]


def path(relative: str) -> Path:
    return ROOT / relative


def read(relative: str) -> str:
    return path(relative).read_text(encoding='utf-8')


def write(relative: str, content: str) -> None:
    path(relative).write_text(content, encoding='utf-8')


def replace(relative: str, old: str, new: str, count: int = 1) -> None:
    content = read(relative)
    actual = content.count(old)
    if actual != count:
        raise SystemExit(f'{relative}: expected {count} matches, found {actual}: {old[:100]!r}')
    write(relative, content.replace(old, new))


def regex_replace(relative: str, pattern: str, replacement: str, count: int = 1) -> None:
    content = read(relative)
    updated, actual = re.subn(pattern, replacement, content, count=count, flags=re.S)
    if actual != count:
        raise SystemExit(f'{relative}: expected {count} regex matches, found {actual}: {pattern[:100]!r}')
    write(relative, updated)


# Route and layout boundaries.
replace(
    'src/FE/src/routes/index.tsx',
    "import { DesktopOnlySuperadminBoundary } from '../features/superadmin/components/DesktopOnlySuperadmin';\n",
    '',
)
regex_replace(
    'src/FE/src/routes/index.tsx',
    r'<DesktopOnlySuperadminBoundary>\s*<AppLayout />\s*</DesktopOnlySuperadminBoundary>',
    '<AppLayout />',
    count=2,
)

replace(
    'src/FE/src/components/layouts/AppLayout.tsx',
    "import {\n  DesktopOnlySuperadminScreen,\n} from '../../features/superadmin/components/DesktopOnlySuperadmin';\nimport { isDesktopPlatform } from '../../lib/platform';\n",
    '',
)
replace('src/FE/src/components/layouts/AppLayout.tsx', '  const isDesktop = isDesktopPlatform();\n', '')
regex_replace(
    'src/FE/src/components/layouts/AppLayout.tsx',
    r"\n  if \(isSuperadmin && !isDesktop\) \{\s*return <DesktopOnlySuperadminScreen onLogout=\{handleLogout\} />;\s*\}\n",
    '\n',
)
regex_replace(
    'src/FE/src/components/layouts/AppLayout.tsx',
    r"\{isDesktop && \(\s*(<Can permission=\"organization:manage\">.*?</Can>)\s*\)\}",
    r'\1',
)

# Superadmin page and API calls become platform-neutral.
for old in [
    "import { isDesktopPlatform } from '../../../lib/platform';\n",
    "import { useAuth } from '../../../providers/useAuth';\n",
    "import { DesktopOnlySuperadminScreen } from '../components/DesktopOnlySuperadmin';\n",
]:
    replace('src/FE/src/features/superadmin/routes/SuperAdmin.tsx', old, '')
replace(
    'src/FE/src/features/superadmin/routes/SuperAdmin.tsx',
    "import {\n  activateOrganizationSession,\n  clearOrganizationSession,\n  getOrganizationSession,\n} from '../organizationSession';\n",
    "import {\n  activateOrganizationSession,\n  getOrganizationSession,\n} from '../organizationSession';\n",
)
replace(
    'src/FE/src/features/superadmin/routes/SuperAdmin.tsx',
    '  const { logout } = useAuth();\n  const canUseSuperadmin = isDesktopPlatform();\n',
    '',
)
replace('src/FE/src/features/superadmin/routes/SuperAdmin.tsx', '    enabled: canUseSuperadmin,\n', '')
replace(
    'src/FE/src/features/superadmin/routes/SuperAdmin.tsx',
    '    if (!canUseSuperadmin) return;\n\n',
    '',
    count=2,
)
replace(
    'src/FE/src/features/superadmin/routes/SuperAdmin.tsx',
    '    if (!canUseSuperadmin || !selectedOrganization) return;\n',
    '    if (!selectedOrganization) return;\n',
)
regex_replace(
    'src/FE/src/features/superadmin/routes/SuperAdmin.tsx',
    r"\n  if \(!canUseSuperadmin\) \{.*?\n  \}\n\n  return \(\n    <div className=\"page-container superadmin-page\">",
    '\n  return (\n    <div className="page-container superadmin-page">',
)

replace(
    'src/FE/src/features/superadmin/api.ts',
    "import { assertDesktopSuperadminAvailable } from '../../lib/platform';\n",
    '',
)
replace(
    'src/FE/src/features/superadmin/api.ts',
    '  assertDesktopSuperadminAvailable();\n',
    '',
    count=4,
)

# Delegated-session validation remains; only device policy and startup rewriting are removed.
replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    "import {\n  AUTH_TOKEN_KEY,\n  AuthStorage,\n  REAUTH_IN_FLIGHT_KEY,\n  USER_EMAIL_KEY,\n} from '../../providers/authContextValue';\nimport {\n  assertDesktopSuperadminAvailable,\n  isDesktopPlatform,\n} from '../../lib/platform';\n",
    "import {\n  AUTH_TOKEN_KEY,\n  AuthStorage,\n  REAUTH_IN_FLIGHT_KEY,\n} from '../../providers/authContextValue';\n",
)
regex_replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    r"\nexport type SuperadminSessionNormalizationResult =.*?;\n\ninterface StoredOrganizationSessionState",
    '\ninterface StoredOrganizationSessionState',
)
regex_replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    r"\ntype NormalizationAction =.*?;\n\nexport function getOrganizationSession",
    '\nexport function getOrganizationSession',
)
replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    '  assertDesktopSuperadminAvailable();\n\n',
    '',
)
regex_replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    r"\n/\*\*\n \* Normalizes persisted Superadmin state.*?\nexport function clearOrganizationSession",
    '\nexport function clearOrganizationSession',
)
regex_replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    r"\nfunction selectNormalizationAction\(.*?\nfunction isValidRecoveryPair",
    '\nfunction isValidRecoveryPair',
)
regex_replace(
    'src/FE/src/features/superadmin/organizationSession.ts',
    r"\nfunction clearAuthenticationAndOrganizationSession\(\): void \{.*?\n\}\n?$",
    '\n',
)

replace(
    'src/FE/src/main.tsx',
    "import { normalizeSuperadminSessionForCurrentPlatform } from './features/superadmin/organizationSession';\n",
    '',
)
replace('src/FE/src/main.tsx', '  normalizeSuperadminSessionForCurrentPlatform();\n\n', '')

# Blocker CSS is obsolete; responsive Superadmin styles remain.
regex_replace(
    'src/FE/src/features/superadmin/routes/SuperAdmin.css',
    r"\.superadmin-desktop-only \{.*?(?=\.superadmin-page-header \{)",
    '',
)

# Focused tests.
write(
    'src/FE/src/features/superadmin/api.test.ts',
    """import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../lib/axios';
import {
  createOrganization,
  createOrganizationSession,
  getOrganizations,
  inviteOrganizationAdmin,
} from './api';

vi.mock('../../lib/axios', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));

describe('Superadmin API', () => {
  beforeEach(() => vi.clearAllMocks());

  it('uses organization endpoints from a mobile browser', async () => {
    vi.stubGlobal('navigator', {
      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',
      maxTouchPoints: 5,
    });

    const onboarding = {
      organization: { id: 'organization-id', name: 'Organisation', cvr: '12345678' },
      user: {
        id: 'user-id', organizationId: 'organization-id', displayName: 'Administrator',
        email: null, phone: null, role: 'Admin', entraInvitationSent: false,
      },
    };
    const session = {
      token: 'delegated-token', tokenType: 'Bearer', expiresIn: 900,
      user: {
        userId: 'user-id', organizationId: 'organization-id',
        email: 'superadmin@example.com', displayName: 'Super Admin', role: 'Superadmin',
      },
    };
    const admin = {
      id: 'admin-id', organizationId: 'organization-id', displayName: 'Administrator',
      email: 'admin@example.com', phone: null, role: 'Admin', entraInvitationSent: true,
    };

    vi.mocked(apiClient.get).mockResolvedValue([]);
    vi.mocked(apiClient.post).mockResolvedValueOnce(onboarding).mockResolvedValueOnce(session);
    vi.mocked(apiClient.put).mockResolvedValue(admin);

    await expect(getOrganizations()).resolves.toEqual([]);
    await expect(createOrganization({
      name: ' Organisation ', cvr: ' 12345678 ', adminDisplayName: ' Administrator ',
    })).resolves.toEqual(onboarding);
    await expect(createOrganizationSession('organization-id')).resolves.toEqual(session);
    await expect(inviteOrganizationAdmin({
      organizationId: 'organization-id', email: ' admin@example.com ',
      displayName: ' Administrator ', phone: '',
    })).resolves.toEqual(admin);

    expect(apiClient.get).toHaveBeenCalledWith('/api/organizations', { skipGlobalErrorToast: true });
    expect(apiClient.post).toHaveBeenNthCalledWith(2,
      '/api/organizations/organization-id/session', undefined, { skipGlobalErrorToast: true });
    expect(apiClient.put).toHaveBeenCalledWith('/api/organizations/organization-id/admin', {
      email: 'admin@example.com', displayName: 'Administrator', phone: null,
    }, { skipGlobalErrorToast: true });
  });
});
""",
)

write(
    'src/FE/src/features/superadmin/organizationSession.test.ts',
    """import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AUTH_TOKEN_KEY, REAUTH_IN_FLIGHT_KEY } from '../../providers/authContextValue';
import {
  activateOrganizationSession,
  getOrganizationSession,
  restoreHomeOrganizationSession,
} from './organizationSession';

const HOME_AUTH_TOKEN_KEY = 'workslip.superadmin.homeAuthToken';
const ORGANIZATION_SESSION_ID_KEY = 'workslip.superadmin.organizationSessionId';
const ORGANIZATION_SESSION_NAME_KEY = 'workslip.superadmin.organizationSessionName';
const NOW_SECONDS = 2_000_000_000;
const ACTOR_ID = '11111111-1111-4111-8111-111111111111';
const OTHER_ACTOR_ID = '22222222-2222-4222-8222-222222222222';
const HOME_ORGANIZATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const CUSTOMER_ORGANIZATION_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

function token(payload: Record<string, unknown>): string {
  return `header.${btoa(JSON.stringify(payload)).replace(/\\+/g, '-').replace(/\\//g, '_').replace(/=+$/g, '')}.signature`;
}

function homePayload(overrides: Record<string, unknown> = {}) {
  return {
    nameid: ACTOR_ID, organizationId: HOME_ORGANIZATION_ID,
    role: 'Superadmin', exp: NOW_SECONDS + 300, ...overrides,
  };
}

function delegatedPayload(overrides: Record<string, unknown> = {}) {
  return {
    nameid: ACTOR_ID, organizationId: CUSTOMER_ORGANIZATION_ID,
    homeOrganizationId: HOME_ORGANIZATION_ID, role: 'Superadmin',
    exp: NOW_SECONDS + 120, delegatedOrganizationSession: true, ...overrides,
  };
}

function saveDelegation(activeToken: string, homeToken?: string) {
  localStorage.setItem(AUTH_TOKEN_KEY, activeToken);
  if (homeToken) localStorage.setItem(HOME_AUTH_TOKEN_KEY, homeToken);
  localStorage.setItem(ORGANIZATION_SESSION_ID_KEY, CUSTOMER_ORGANIZATION_ID);
  localStorage.setItem(ORGANIZATION_SESSION_NAME_KEY, 'NP Teknik');
}

describe('Superadmin organization sessions', () => {
  const validHomeToken = token(homePayload());
  const validDelegatedToken = token(delegatedPayload());

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(NOW_SECONDS * 1000);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it.each(['iPhone', 'Android'])(
    'activates a validated organization session on %s',
    (device) => {
      vi.stubGlobal('navigator', { userAgent: device, maxTouchPoints: 5 });
      localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);

      activateOrganizationSession(
        { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' }, validDelegatedToken,
      );

      expect(localStorage.getItem(HOME_AUTH_TOKEN_KEY)).toBe(validHomeToken);
      expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validDelegatedToken);
      expect(getOrganizationSession()).toEqual({ id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' });
    },
  );

  it('restores a validated home session', () => {
    saveDelegation(validDelegatedToken, validHomeToken);
    localStorage.setItem(REAUTH_IN_FLIGHT_KEY, '123');

    expect(restoreHomeOrganizationSession()).toBe(true);
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
    expect(localStorage.getItem(REAUTH_IN_FLIGHT_KEY)).toBeNull();
    expect(getOrganizationSession()).toBeNull();
  });

  it('restores home after delegated-token expiry', () => {
    saveDelegation(token(delegatedPayload({ exp: NOW_SECONDS })), validHomeToken);
    expect(restoreHomeOrganizationSession()).toBe(true);
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
  });

  it('clears invalid recovery state', () => {
    saveDelegation(validDelegatedToken, token(homePayload({ nameid: OTHER_ACTOR_ID })));
    expect(restoreHomeOrganizationSession()).toBe(false);
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
    expect(getOrganizationSession()).toBeNull();
  });

  it('rejects a delegated token for another actor', () => {
    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);
    expect(() => activateOrganizationSession(
      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },
      token(delegatedPayload({ nameid: OTHER_ACTOR_ID })),
    )).toThrow('Organisationssessionens token kunne ikke valideres.');
  });

  it('rejects malformed organization identifiers', () => {
    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);
    expect(() => activateOrganizationSession(
      { id: 'not-a-uuid', name: 'NP Teknik' },
      token(delegatedPayload({ organizationId: undefined })),
    )).toThrow('Organisationssessionens token kunne ikke valideres.');
  });
});
""",
)

# Current contract and historical decision record.
replace(
    'Docs/api/contract.md',
    "The official frontend makes Superadmin organization administration and delegated\norganization sessions available only on desktop-class devices. On iOS, Android,\nand iPadOS a valid delegated recovery state is restored to the home Superadmin\ntoken before authentication bootstrap and then shows an authenticated\ndesktop-only blocker. An expired delegated token can still restore a matching,\nunexpired home token; a missing or expired home token, malformed claims,\ncross-actor state, or organization-inconsistent recovery state is cleared and\nrequires a new login.\nThis is a frontend product boundary, not a bearer-token security guarantee: API\nclients must rely on the authorization policies and token validation documented\nabove rather than device detection.\n",
    "The official frontend exposes Superadmin organization administration and delegated\norganization sessions across desktop browsers, mobile browsers, and installed PWA\ncontexts. Device family and viewport size do not change access. API clients must\nrely on the authorization policies and delegated-token validation documented above.\n",
)

spec_path = 'Docs/superpowers/specs/spec-desktop-only-superadmin-sessions.md'
spec = read(spec_path)
spec = spec.replace(
    "title: 'Desktop-only Superadmin organization sessions'\n",
    "title: 'Desktop-only Superadmin organization sessions (superseded)'\n",
    1,
).replace(
    "status: 'done'\n",
    "status: 'superseded'\nsuperseded_by: 'WOR-237'\n",
    1,
)
marker = '---\n\n<frozen-after-approval'
if marker not in spec:
    raise SystemExit('Historical spec marker not found')
spec = spec.replace(
    marker,
    "---\n\n> **Superseded on 2026-07-31 by WOR-237.** Superadmin organization administration and delegated organization sessions are now supported on every frontend platform. The frozen section below records the former product decision and is retained only as history.\n\n<frozen-after-approval",
    1,
)
write(spec_path, spec)

# Delete the obsolete product boundary and its tests.
for relative in [
    'src/FE/src/features/superadmin/components/DesktopOnlySuperadmin.tsx',
    'src/FE/src/features/superadmin/components/DesktopOnlySuperadmin.test.tsx',
    'src/FE/src/components/layouts/AppLayout.desktopOnly.test.tsx',
    'src/FE/src/lib/platform.ts',
    'src/FE/src/lib/platform.test.ts',
]:
    target = path(relative)
    if not target.exists():
        raise SystemExit(f'Expected obsolete file missing: {relative}')
    target.unlink()

# Temporary trigger must never enter the product commit.
trigger = path('src/FE/src/wor-237-validation-trigger.txt')
if trigger.exists():
    trigger.unlink()

for root_relative in ['src/FE/src', 'Docs/api', 'Docs/architecture']:
    for candidate in path(root_relative).rglob('*'):
        if not candidate.is_file() or candidate.suffix not in {'.ts', '.tsx', '.md', '.css'}:
            continue
        content = candidate.read_text(encoding='utf-8')
        for forbidden in [
            'DesktopOnlySuperadmin', 'isDesktopPlatform',
            'assertDesktopSuperadminAvailable',
            'normalizeSuperadminSessionForCurrentPlatform',
            'Superadmin er kun tilgængelig på computer', 'desktop-only blocker',
        ]:
            if forbidden in content:
                raise SystemExit(f'Legacy reference {forbidden!r} remains in {candidate}')

print('WOR-237 transform completed')
