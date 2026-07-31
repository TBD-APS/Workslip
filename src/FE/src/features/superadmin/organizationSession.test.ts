import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
  return `header.${btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')}.signature`;
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
