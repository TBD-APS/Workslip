from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def write(relative: str, content: str) -> None:
    (ROOT / relative).write_text(content, encoding="utf-8")


def replace_exact(relative: str, old: str, new: str, expected: int = 1) -> None:
    content = read(relative)
    count = content.count(old)
    if count != expected:
        raise SystemExit(
            f"Expected {expected} occurrence(s) in {relative}, found {count}: {old[:120]!r}"
        )
    write(relative, content.replace(old, new))


def remove_between(relative: str, start: str, end: str) -> None:
    content = read(relative)
    start_index = content.find(start)
    if start_index < 0:
        raise SystemExit(f"Start marker not found in {relative}: {start!r}")
    end_index = content.find(end, start_index)
    if end_index < 0:
        raise SystemExit(f"End marker not found in {relative}: {end!r}")
    write(relative, content[:start_index] + content[end_index:])


# Router: remove the device boundary from both authenticated route trees.
replace_exact(
    "src/FE/src/routes/index.tsx",
    "import { DesktopOnlySuperadminBoundary } from '../features/superadmin/components/DesktopOnlySuperadmin';\n",
    "",
)
replace_exact(
    "src/FE/src/routes/index.tsx",
    """            <DesktopOnlySuperadminBoundary>\n              <AppLayout />\n            </DesktopOnlySuperadminBoundary>""",
    "            <AppLayout />",
    expected=2,
)

# Layout: remove the mobile blocker and expose the existing Superadmin affordance everywhere.
replace_exact(
    "src/FE/src/components/layouts/AppLayout.tsx",
    """import {\n  DesktopOnlySuperadminScreen,\n} from '../../features/superadmin/components/DesktopOnlySuperadmin';\nimport { isDesktopPlatform } from '../../lib/platform';\n""",
    "",
)
replace_exact(
    "src/FE/src/components/layouts/AppLayout.tsx",
    "  const isDesktop = isDesktopPlatform();\n",
    "",
)
replace_exact(
    "src/FE/src/components/layouts/AppLayout.tsx",
    """  if (isSuperadmin && !isDesktop) {\n    return <DesktopOnlySuperadminScreen onLogout={handleLogout} />;\n  }\n\n""",
    "",
)
replace_exact(
    "src/FE/src/components/layouts/AppLayout.tsx",
    """          {isDesktop && (\n            <Can permission=\"organization:manage\">\n              <button\n                type=\"button\"\n                onClick={() => navigate('/superadmin')}\n                className=\"user-avatar\"\n                aria-label=\"Superadmin\"\n                title=\"Superadmin\"\n                aria-current={location.pathname === '/superadmin' ? 'page' : undefined}\n              >\n                <ShieldCheck size={18} />\n              </button>\n            </Can>\n          )}\n""",
    """          <Can permission=\"organization:manage\">\n            <button\n              type=\"button\"\n              onClick={() => navigate('/superadmin')}\n              className=\"user-avatar\"\n              aria-label=\"Superadmin\"\n              title=\"Superadmin\"\n              aria-current={location.pathname === '/superadmin' ? 'page' : undefined}\n            >\n              <ShieldCheck size={18} />\n            </button>\n          </Can>\n""",
)

# Superadmin page: queries and actions are platform-neutral.
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "import { isDesktopPlatform } from '../../../lib/platform';\n",
    "",
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "import { useAuth } from '../../../providers/useAuth';\n",
    "",
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    """import {\n  activateOrganizationSession,\n  clearOrganizationSession,\n  getOrganizationSession,\n} from '../organizationSession';\n""",
    """import {\n  activateOrganizationSession,\n  getOrganizationSession,\n} from '../organizationSession';\n""",
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "import { DesktopOnlySuperadminScreen } from '../components/DesktopOnlySuperadmin';\n",
    "",
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "  const { logout } = useAuth();\n  const canUseSuperadmin = isDesktopPlatform();\n",
    "",
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "    enabled: canUseSuperadmin,\n",
    "",
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "    if (!canUseSuperadmin) return;\n\n",
    "",
    expected=2,
)
replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "    if (!canUseSuperadmin || !selectedOrganization) return;\n",
    "    if (!selectedOrganization) return;\n",
)
remove_between(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "  if (!canUseSuperadmin) {\n",
    "  return (\n",
)

# API client: backend authorization is the boundary; device family is irrelevant.
replace_exact(
    "src/FE/src/features/superadmin/api.ts",
    "import { assertDesktopSuperadminAvailable } from '../../lib/platform';\n",
    "",
)
replace_exact(
    "src/FE/src/features/superadmin/api.ts",
    "  assertDesktopSuperadminAvailable();\n",
    "",
    expected=4,
)

# Delegated sessions: keep identity and tenant validation, remove device policy and startup rewriting.
replace_exact(
    "src/FE/src/features/superadmin/organizationSession.ts",
    """import {\n  AUTH_TOKEN_KEY,\n  AuthStorage,\n  REAUTH_IN_FLIGHT_KEY,\n  USER_EMAIL_KEY,\n} from '../../providers/authContextValue';\nimport {\n  assertDesktopSuperadminAvailable,\n  isDesktopPlatform,\n} from '../../lib/platform';\n""",
    """import {\n  AUTH_TOKEN_KEY,\n  AuthStorage,\n  REAUTH_IN_FLIGHT_KEY,\n} from '../../providers/authContextValue';\n""",
)
remove_between(
    "src/FE/src/features/superadmin/organizationSession.ts",
    "export type SuperadminSessionNormalizationResult =\n",
    "interface StoredOrganizationSessionState",
)
remove_between(
    "src/FE/src/features/superadmin/organizationSession.ts",
    "type NormalizationAction =\n",
    "export function getOrganizationSession",
)
replace_exact(
    "src/FE/src/features/superadmin/organizationSession.ts",
    "  assertDesktopSuperadminAvailable();\n\n",
    "",
)
remove_between(
    "src/FE/src/features/superadmin/organizationSession.ts",
    "/**\n * Normalizes persisted Superadmin state before AuthProvider can read the\n",
    "export function clearOrganizationSession",
)
remove_between(
    "src/FE/src/features/superadmin/organizationSession.ts",
    "function selectNormalizationAction(\n",
    "function isValidRecoveryPair",
)
replace_exact(
    "src/FE/src/features/superadmin/organizationSession.ts",
    """\nfunction clearAuthenticationAndOrganizationSession(): void {\n  // Remove the potentially customer-scoped credential before its metadata.\n  AuthStorage.removeItem(AUTH_TOKEN_KEY);\n  AuthStorage.removeItem(USER_EMAIL_KEY);\n  AuthStorage.removeItem(REAUTH_IN_FLIGHT_KEY);\n  clearOrganizationSession();\n}\n""",
    "\n",
)

# Startup no longer rewrites delegated sessions based on device family.
replace_exact(
    "src/FE/src/main.tsx",
    "import { normalizeSuperadminSessionForCurrentPlatform } from './features/superadmin/organizationSession';\n",
    "",
)
replace_exact(
    "src/FE/src/main.tsx",
    "  normalizeSuperadminSessionForCurrentPlatform();\n\n",
    "",
)

# Remove obsolete blocker styling.
css_path = "src/FE/src/features/superadmin/routes/SuperAdmin.css"
css = read(css_path)
start = css.find(".superadmin-desktop-only {")
end = css.find(".superadmin-page-header {", start)
if start < 0 or end < 0:
    raise SystemExit("Desktop-only CSS block markers not found")
write(css_path, css[:start] + css[end:])

# Focused API regression: mobile clients use the same authorized endpoints.
write(
    "src/FE/src/features/superadmin/api.test.ts",
    """import { beforeEach, describe, expect, it, vi } from 'vitest';\nimport { apiClient } from '../../lib/axios';\nimport {\n  createOrganization,\n  createOrganizationSession,\n  getOrganizations,\n  inviteOrganizationAdmin,\n} from './api';\n\nvi.mock('../../lib/axios', () => ({\n  apiClient: {\n    get: vi.fn(),\n    post: vi.fn(),\n    put: vi.fn(),\n  },\n}));\n\ndescribe('Superadmin API', () => {\n  beforeEach(() => {\n    vi.clearAllMocks();\n    vi.unstubAllGlobals();\n  });\n\n  it('uses every organization endpoint from a mobile browser', async () => {\n    vi.stubGlobal('navigator', {\n      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',\n      maxTouchPoints: 5,\n    });\n\n    const onboarding = {\n      organization: {\n        id: 'organization-id',\n        name: 'Organisation',\n        cvr: '12345678',\n      },\n      user: {\n        id: 'user-id',\n        organizationId: 'organization-id',\n        displayName: 'Administrator',\n        email: null,\n        phone: null,\n        role: 'Admin',\n        entraInvitationSent: false,\n      },\n    };\n    const session = { token: 'delegated-token', expiresUtc: '2026-07-31T14:00:00Z' };\n    const admin = {\n      id: 'admin-id',\n      organizationId: 'organization-id',\n      displayName: 'Administrator',\n      email: 'admin@example.com',\n      phone: null,\n      role: 'Admin',\n      entraInvitationSent: true,\n    };\n\n    vi.mocked(apiClient.get).mockResolvedValue([]);\n    vi.mocked(apiClient.post)\n      .mockResolvedValueOnce(onboarding)\n      .mockResolvedValueOnce(session);\n    vi.mocked(apiClient.put).mockResolvedValue(admin);\n\n    await expect(getOrganizations()).resolves.toEqual([]);\n    await expect(createOrganization({\n      name: ' Organisation ',\n      cvr: ' 12345678 ',\n      adminDisplayName: ' Administrator ',\n    })).resolves.toEqual(onboarding);\n    await expect(createOrganizationSession('organization-id')).resolves.toEqual(session);\n    await expect(inviteOrganizationAdmin({\n      organizationId: 'organization-id',\n      email: ' admin@example.com ',\n      displayName: ' Administrator ',\n      phone: '',\n    })).resolves.toEqual(admin);\n\n    expect(apiClient.get).toHaveBeenCalledWith('/api/organizations', {\n      skipGlobalErrorToast: true,\n    });\n    expect(apiClient.post).toHaveBeenNthCalledWith(1, '/api/organizations', {\n      name: 'Organisation',\n      cvr: '12345678',\n      adminDisplayName: 'Administrator',\n      adminEmail: null,\n      adminPhone: null,\n    }, {\n      skipGlobalErrorToast: true,\n    });\n    expect(apiClient.post).toHaveBeenNthCalledWith(2, '/api/organizations/organization-id/session', undefined, {\n      skipGlobalErrorToast: true,\n    });\n    expect(apiClient.put).toHaveBeenCalledWith('/api/organizations/organization-id/admin', {\n      email: 'admin@example.com',\n      displayName: 'Administrator',\n      phone: null,\n    }, {\n      skipGlobalErrorToast: true,\n    });\n  });\n});\n""",
)

# Focused delegated-session tests: mobile activation is supported; token validation remains fail-closed.
write(
    "src/FE/src/features/superadmin/organizationSession.test.ts",
    """import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';\nimport {\n  AUTH_TOKEN_KEY,\n  REAUTH_IN_FLIGHT_KEY,\n} from '../../providers/authContextValue';\nimport {\n  activateOrganizationSession,\n  getOrganizationSession,\n  restoreHomeOrganizationSession,\n} from './organizationSession';\n\nconst HOME_AUTH_TOKEN_KEY = 'workslip.superadmin.homeAuthToken';\nconst ORGANIZATION_SESSION_ID_KEY = 'workslip.superadmin.organizationSessionId';\nconst ORGANIZATION_SESSION_NAME_KEY = 'workslip.superadmin.organizationSessionName';\nconst NOW_SECONDS = 2_000_000_000;\nconst ACTOR_ID = '11111111-1111-4111-8111-111111111111';\nconst OTHER_ACTOR_ID = '22222222-2222-4222-8222-222222222222';\nconst HOME_ORGANIZATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';\nconst CUSTOMER_ORGANIZATION_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';\n\nfunction token(payload: Record<string, unknown>): string {\n  const encodedPayload = globalThis.btoa(JSON.stringify(payload))\n    .replace(/\\+/g, '-')\n    .replace(/\\//g, '_')\n    .replace(/=+$/g, '');\n  return `header.${encodedPayload}.signature`;\n}\n\nfunction homePayload(\n  overrides: Record<string, unknown> = {},\n): Record<string, unknown> {\n  return {\n    nameid: ACTOR_ID,\n    organizationId: HOME_ORGANIZATION_ID,\n    role: 'Superadmin',\n    exp: NOW_SECONDS + 300,\n    ...overrides,\n  };\n}\n\nfunction delegatedPayload(\n  overrides: Record<string, unknown> = {},\n): Record<string, unknown> {\n  return {\n    nameid: ACTOR_ID,\n    organizationId: CUSTOMER_ORGANIZATION_ID,\n    homeOrganizationId: HOME_ORGANIZATION_ID,\n    role: 'Superadmin',\n    exp: NOW_SECONDS + 120,\n    delegatedOrganizationSession: true,\n    ...overrides,\n  };\n}\n\nfunction useMobileDevice(device: 'ios' | 'android'): void {\n  vi.stubGlobal('navigator', device === 'ios'\n    ? {\n      userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',\n      maxTouchPoints: 5,\n    }\n    : {\n      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',\n      maxTouchPoints: 5,\n    });\n}\n\nfunction saveDelegation(activeToken: string, homeToken?: string): void {\n  localStorage.setItem(AUTH_TOKEN_KEY, activeToken);\n  if (homeToken) localStorage.setItem(HOME_AUTH_TOKEN_KEY, homeToken);\n  localStorage.setItem(ORGANIZATION_SESSION_ID_KEY, CUSTOMER_ORGANIZATION_ID);\n  localStorage.setItem(ORGANIZATION_SESSION_NAME_KEY, 'NP Teknik');\n}\n\ndescribe('Superadmin organization sessions', () => {\n  const validHomeToken = token(homePayload());\n  const validDelegatedToken = token(delegatedPayload());\n\n  beforeEach(() => {\n    localStorage.clear();\n    vi.restoreAllMocks();\n    vi.unstubAllGlobals();\n    vi.useFakeTimers();\n    vi.setSystemTime(NOW_SECONDS * 1000);\n  });\n\n  afterEach(() => {\n    vi.useRealTimers();\n    vi.unstubAllGlobals();\n  });\n\n  it('validates the recovery pair during explicit restore', () => {\n    saveDelegation(validDelegatedToken, validHomeToken);\n    localStorage.setItem(REAUTH_IN_FLIGHT_KEY, '123');\n\n    expect(restoreHomeOrganizationSession()).toBe(true);\n    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);\n    expect(localStorage.getItem(REAUTH_IN_FLIGHT_KEY)).toBeNull();\n    expect(getOrganizationSession()).toBeNull();\n  });\n\n  it('clears invalid state during explicit restore', () => {\n    saveDelegation(validDelegatedToken, token(homePayload({\n      nameid: OTHER_ACTOR_ID,\n    })));\n\n    expect(restoreHomeOrganizationSession()).toBe(false);\n    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();\n    expect(getOrganizationSession()).toBeNull();\n  });\n\n  it('allows explicit home restoration after delegated-token expiry', () => {\n    saveDelegation(\n      token(delegatedPayload({ exp: NOW_SECONDS })),\n      validHomeToken,\n    );\n\n    expect(restoreHomeOrganizationSession()).toBe(true);\n    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);\n  });\n\n  it.each(['ios', 'android'] as const)(\n    'activates a validated organization session on %s',\n    (device) => {\n      useMobileDevice(device);\n      localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);\n\n      activateOrganizationSession(\n        { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },\n        validDelegatedToken,\n      );\n\n      expect(localStorage.getItem(HOME_AUTH_TOKEN_KEY)).toBe(validHomeToken);\n      expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validDelegatedToken);\n      expect(getOrganizationSession()).toEqual({\n        id: CUSTOMER_ORGANIZATION_ID,\n        name: 'NP Teknik',\n      });\n    },\n  );\n\n  it('accepts only a matching delegated token during activation', () => {\n    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);\n\n    activateOrganizationSession(\n      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },\n      validDelegatedToken,\n    );\n    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validDelegatedToken);\n\n    expect(() => activateOrganizationSession(\n      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },\n      token(delegatedPayload({ nameid: OTHER_ACTOR_ID })),\n    )).toThrow('Organisationssessionens token kunne ikke valideres.');\n  });\n\n  it('rejects malformed selected and delegated organization identifiers', () => {\n    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);\n\n    expect(() => activateOrganizationSession(\n      { id: 'not-a-uuid', name: 'NP Teknik' },\n      token(delegatedPayload({ organizationId: undefined })),\n    )).toThrow('Organisationssessionens token kunne ikke valideres.');\n  });\n\n  it('rejects activation when active state belongs to another actor', () => {\n    localStorage.setItem(HOME_AUTH_TOKEN_KEY, validHomeToken);\n    localStorage.setItem(AUTH_TOKEN_KEY, token(homePayload({\n      nameid: OTHER_ACTOR_ID,\n    })));\n\n    expect(() => activateOrganizationSession(\n      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },\n      validDelegatedToken,\n    )).toThrow('Organisationssessionens token kunne ikke valideres.');\n  });\n});\n""",
)

# Active contract now documents platform-neutral availability.
replace_exact(
    "Docs/api/contract.md",
    """The official frontend makes Superadmin organization administration and delegated\norganization sessions available only on desktop-class devices. On iOS, Android,\nand iPadOS a valid delegated recovery state is restored to the home Superadmin\ntoken before authentication bootstrap and then shows an authenticated\ndesktop-only blocker. An expired delegated token can still restore a matching,\nunexpired home token; a missing or expired home token, malformed claims,\ncross-actor state, or organization-inconsistent recovery state is cleared and\nrequires a new login.\nThis is a frontend product boundary, not a bearer-token security guarantee: API\nclients must rely on the authorization policies and token validation documented\nabove rather than device detection.\n""",
    """The official frontend exposes Superadmin organization administration and delegated\norganization sessions across desktop browsers, mobile browsers, and installed PWA\ncontexts. Device family and viewport size do not change access. API clients must\nrely on the authorization policies and delegated-token validation documented above.\n""",
)

# Preserve the old frozen product decision as clearly superseded history.
spec_path = "Docs/superpowers/specs/spec-desktop-only-superadmin-sessions.md"
spec = read(spec_path)
spec = spec.replace(
    "title: 'Desktop-only Superadmin organization sessions'\n",
    "title: 'Desktop-only Superadmin organization sessions (superseded)'\n",
    1,
)
spec = spec.replace("status: 'done'\n", "status: 'superseded'\nsuperseded_by: 'WOR-237'\n", 1)
marker = "---\n\n<frozen-after-approval"
replacement = """---\n\n> **Superseded on 2026-07-31 by WOR-237.** Superadmin organization administration and delegated organization sessions are now supported on every frontend platform. The frozen section below records the former product decision and is retained only as history.\n\n<frozen-after-approval"""
if marker not in spec:
    raise SystemExit("Superseded-spec insertion marker not found")
spec = spec.replace(marker, replacement, 1)
write(spec_path, spec)

# Delete obsolete device-policy implementation and tests.
for relative in [
    "src/FE/src/features/superadmin/components/DesktopOnlySuperadmin.tsx",
    "src/FE/src/features/superadmin/components/DesktopOnlySuperadmin.test.tsx",
    "src/FE/src/components/layouts/AppLayout.desktopOnly.test.tsx",
    "src/FE/src/lib/platform.ts",
    "src/FE/src/lib/platform.test.ts",
]:
    path = ROOT / relative
    if not path.exists():
        raise SystemExit(f"Expected obsolete file not found: {relative}")
    path.unlink()

# Static guard: no active frontend code may retain the removed product boundary.
for relative in [
    "src/FE/src",
    "Docs/api",
    "Docs/architecture",
]:
    root = ROOT / relative
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix not in {".ts", ".tsx", ".md", ".css"}:
            continue
        content = path.read_text(encoding="utf-8")
        for forbidden in [
            "DesktopOnlySuperadmin",
            "isDesktopPlatform",
            "assertDesktopSuperadminAvailable",
            "normalizeSuperadminSessionForCurrentPlatform",
            "Superadmin er kun tilgængelig på computer",
            "desktop-only blocker",
        ]:
            if forbidden in content:
                raise SystemExit(f"Forbidden legacy reference {forbidden!r} remains in {path}")

print("WOR-237 transform completed")
