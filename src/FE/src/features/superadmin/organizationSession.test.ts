import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  AUTH_TOKEN_KEY,
  AuthStorage,
  REAUTH_IN_FLIGHT_KEY,
  USER_EMAIL_KEY,
} from '../../providers/authContextValue';
import {
  activateOrganizationSession,
  getOrganizationSession,
  normalizeSuperadminSessionForCurrentPlatform,
  restoreHomeOrganizationSession,
} from './organizationSession';

const HOME_AUTH_TOKEN_KEY = 'workslip.superadmin.homeAuthToken';
const ORGANIZATION_SESSION_ID_KEY = 'workslip.superadmin.organizationSessionId';
const ORGANIZATION_SESSION_NAME_KEY = 'workslip.superadmin.organizationSessionName';
const NOW_SECONDS = 2_000_000_000;
const ACTOR_ID = '11111111-1111-4111-8111-111111111111';
const OTHER_ACTOR_ID = '22222222-2222-4222-8222-222222222222';
const HOME_ORGANIZATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const OTHER_HOME_ORGANIZATION_ID = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const CUSTOMER_ORGANIZATION_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

function token(payload: Record<string, unknown>): string {
  const encodedPayload = globalThis.btoa(JSON.stringify(payload))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '');
  return `header.${encodedPayload}.signature`;
}

function homePayload(
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    nameid: ACTOR_ID,
    organizationId: HOME_ORGANIZATION_ID,
    role: 'Superadmin',
    exp: NOW_SECONDS + 300,
    ...overrides,
  };
}

function delegatedPayload(
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    nameid: ACTOR_ID,
    organizationId: CUSTOMER_ORGANIZATION_ID,
    homeOrganizationId: HOME_ORGANIZATION_ID,
    role: 'Superadmin',
    exp: NOW_SECONDS + 120,
    delegatedOrganizationSession: true,
    ...overrides,
  };
}

function useDevice(device: 'desktop' | 'ios' | 'android'): void {
  const values = {
    desktop: {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
      maxTouchPoints: 0,
    },
    ios: {
      userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',
      maxTouchPoints: 5,
    },
    android: {
      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',
      maxTouchPoints: 5,
    },
  };
  vi.stubGlobal('navigator', values[device]);
}

function saveDelegation(activeToken: string, homeToken?: string): void {
  localStorage.setItem(AUTH_TOKEN_KEY, activeToken);
  if (homeToken) localStorage.setItem(HOME_AUTH_TOKEN_KEY, homeToken);
  localStorage.setItem(ORGANIZATION_SESSION_ID_KEY, CUSTOMER_ORGANIZATION_ID);
  localStorage.setItem(ORGANIZATION_SESSION_NAME_KEY, 'NP Teknik');
}

describe('Superadmin organization session recovery', () => {
  const validHomeToken = token(homePayload());
  const validDelegatedToken = token(delegatedPayload());

  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useFakeTimers();
    vi.setSystemTime(NOW_SECONDS * 1000);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('keeps desktop delegated sessions unchanged', () => {
    useDevice('desktop');
    saveDelegation(validDelegatedToken, validHomeToken);

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('unchanged');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validDelegatedToken);
    expect(getOrganizationSession()?.name).toBe('NP Teknik');
  });

  it.each(['ios', 'android'] as const)(
    'restores a matching, unexpired home token before bootstrap on %s',
    (device) => {
      useDevice(device);
      saveDelegation(validDelegatedToken, validHomeToken);
      localStorage.setItem(REAUTH_IN_FLIGHT_KEY, 'still-set');

      expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('home-restored');
      expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
      expect(localStorage.getItem(REAUTH_IN_FLIGHT_KEY)).toBeNull();
      expect(getOrganizationSession()).toBeNull();
    },
  );

  it.each([
    ['missing home token', validDelegatedToken, undefined],
    ['malformed home token', validDelegatedToken, 'not-a-jwt'],
    ['expired home token', validDelegatedToken, token(homePayload({ exp: NOW_SECONDS }))],
    ['different actor', validDelegatedToken, token(homePayload({ nameid: OTHER_ACTOR_ID }))],
    [
      'different home organization',
      validDelegatedToken,
      token(homePayload({ organizationId: OTHER_HOME_ORGANIZATION_ID })),
    ],
    [
      'delegated home token',
      validDelegatedToken,
      token(homePayload({ delegatedOrganizationSession: true })),
    ],
    [
      'malformed home delegation claim',
      validDelegatedToken,
      token(homePayload({ delegatedOrganizationSession: 'invalid' })),
    ],
    [
      'missing delegated actor',
      token(delegatedPayload({ nameid: undefined })),
      validHomeToken,
    ],
    [
      'missing delegated home organization',
      token(delegatedPayload({ homeOrganizationId: undefined })),
      validHomeToken,
    ],
    [
      'missing delegated expiry',
      token(delegatedPayload({ exp: undefined })),
      validHomeToken,
    ],
    [
      'non-numeric delegated expiry',
      token(delegatedPayload({ exp: 'later' })),
      validHomeToken,
    ],
    [
      'missing delegated organization',
      token(delegatedPayload({ organizationId: undefined })),
      validHomeToken,
    ],
    [
      'conflicting delegated actor aliases',
      token(delegatedPayload({ sub: OTHER_ACTOR_ID })),
      validHomeToken,
    ],
    [
      'missing delegated-session claim',
      token(delegatedPayload({ delegatedOrganizationSession: undefined })),
      validHomeToken,
    ],
    ['malformed active token', 'header.not-json.signature', validHomeToken],
    [
      'active token missing its role',
      token(delegatedPayload({ role: undefined })),
      validHomeToken,
    ],
  ])('fails closed for %s', (_name, activeToken, homeToken) => {
    useDevice('ios');
    saveDelegation(activeToken, homeToken);
    localStorage.setItem(USER_EMAIL_KEY, 'superadmin@workslip.dk');
    localStorage.setItem(REAUTH_IN_FLIGHT_KEY, '123');

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('authentication-cleared');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(USER_EMAIL_KEY)).toBeNull();
    expect(localStorage.getItem(REAUTH_IN_FLIGHT_KEY)).toBeNull();
    expect(getOrganizationSession()).toBeNull();
  });

  it('fails closed when delegated recovery metadata is incomplete', () => {
    useDevice('ios');
    saveDelegation(validDelegatedToken, validHomeToken);
    localStorage.removeItem(ORGANIZATION_SESSION_ID_KEY);

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('authentication-cleared');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
  });

  it('fails closed when stored organization differs from the delegated token', () => {
    useDevice('ios');
    saveDelegation(validDelegatedToken, validHomeToken);
    localStorage.setItem(ORGANIZATION_SESSION_ID_KEY, OTHER_HOME_ORGANIZATION_ID);

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('authentication-cleared');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
  });

  it('restores a valid home token after the delegated token expires', () => {
    useDevice('ios');
    saveDelegation(
      token(delegatedPayload({ exp: NOW_SECONDS })),
      validHomeToken,
    );

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('home-restored');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
    expect(getOrganizationSession()).toBeNull();
  });

  it('clears a malformed active token even without delegation metadata', () => {
    useDevice('android');
    localStorage.setItem(AUTH_TOKEN_KEY, 'malformed');

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('authentication-cleared');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
  });

  it('clears stale metadata while retaining an unexpired home login', () => {
    useDevice('ios');
    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);
    localStorage.setItem(HOME_AUTH_TOKEN_KEY, validHomeToken);
    localStorage.setItem(ORGANIZATION_SESSION_ID_KEY, CUSTOMER_ORGANIZATION_ID);

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('delegation-cleared');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
    expect(getOrganizationSession()).toBeNull();
    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('unchanged');
  });

  it('does not change an ordinary mobile user session', () => {
    useDevice('android');
    const ordinaryToken = token(homePayload({ role: 'User' }));
    localStorage.setItem(AUTH_TOKEN_KEY, ordinaryToken);

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('unchanged');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(ordinaryToken);
  });

  it('re-checks shared storage before invalid recovery cleanup', () => {
    useDevice('ios');
    saveDelegation(validDelegatedToken, 'not-a-jwt');
    const originalGetItem = AuthStorage.getItem.bind(AuthStorage);
    let activeTokenReads = 0;

    vi.spyOn(AuthStorage, 'getItem').mockImplementation((key) => {
      if (key === AUTH_TOKEN_KEY && ++activeTokenReads === 2) {
        localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);
        return validHomeToken;
      }
      return originalGetItem(key);
    });

    expect(normalizeSuperadminSessionForCurrentPlatform()).toBe('delegation-cleared');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
  });

  it('validates the recovery pair during explicit restore', () => {
    useDevice('desktop');
    saveDelegation(validDelegatedToken, validHomeToken);
    localStorage.setItem(REAUTH_IN_FLIGHT_KEY, '123');

    expect(restoreHomeOrganizationSession()).toBe(true);
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
    expect(localStorage.getItem(REAUTH_IN_FLIGHT_KEY)).toBeNull();
  });

  it('clears invalid state during explicit restore', () => {
    useDevice('desktop');
    saveDelegation(validDelegatedToken, token(homePayload({
      nameid: OTHER_ACTOR_ID,
    })));

    expect(restoreHomeOrganizationSession()).toBe(false);
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBeNull();
    expect(getOrganizationSession()).toBeNull();
  });

  it('allows explicit home restoration after delegated-token expiry', () => {
    useDevice('desktop');
    saveDelegation(
      token(delegatedPayload({ exp: NOW_SECONDS })),
      validHomeToken,
    );

    expect(restoreHomeOrganizationSession()).toBe(true);
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
  });

  it('refuses organization-session activation on mobile', () => {
    useDevice('ios');
    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);

    expect(() => activateOrganizationSession(
      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },
      validDelegatedToken,
    )).toThrow('Superadmin er kun tilgængelig på computer.');
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validHomeToken);
  });

  it('accepts only a matching delegated token during desktop activation', () => {
    useDevice('desktop');
    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);

    activateOrganizationSession(
      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },
      validDelegatedToken,
    );
    expect(localStorage.getItem(AUTH_TOKEN_KEY)).toBe(validDelegatedToken);

    expect(() => activateOrganizationSession(
      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },
      token(delegatedPayload({ nameid: OTHER_ACTOR_ID })),
    )).toThrow('Organisationssessionens token kunne ikke valideres.');
  });

  it('rejects malformed selected and delegated organization identifiers', () => {
    useDevice('desktop');
    localStorage.setItem(AUTH_TOKEN_KEY, validHomeToken);

    expect(() => activateOrganizationSession(
      { id: 'not-a-uuid', name: 'NP Teknik' },
      token(delegatedPayload({ organizationId: undefined })),
    )).toThrow('Organisationssessionens token kunne ikke valideres.');
  });

  it('rejects activation when active state belongs to another actor', () => {
    useDevice('desktop');
    localStorage.setItem(HOME_AUTH_TOKEN_KEY, validHomeToken);
    localStorage.setItem(AUTH_TOKEN_KEY, token(homePayload({
      nameid: OTHER_ACTOR_ID,
    })));

    expect(() => activateOrganizationSession(
      { id: CUSTOMER_ORGANIZATION_ID, name: 'NP Teknik' },
      validDelegatedToken,
    )).toThrow('Organisationssessionens token kunne ikke valideres.');
  });
});
