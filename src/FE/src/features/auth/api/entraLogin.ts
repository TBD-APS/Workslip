import { apiClient } from '../../../lib/axios';
import type { AuthTokenResponse } from '../../../api/generated/models';

const PKCE_KEY = 'workslip.loginPkce';

interface PkceState {
  state: string;
  codeVerifier: string;
  redirectUri: string;
  returnTo?: string;
}

interface StartEntraLoginOptions {
  returnTo?: string;
  prompt?: 'none' | 'select_account' | 'login';
}

export interface CompleteEntraLoginResult {
  auth: AuthTokenResponse;
  returnTo: string;
}

/**
 * Thrown when Microsoft silently refuses a reauth (`prompt=none`) because the
 * user has no SSO cookie, needs to consent, or must re-enter credentials. The
 * Login page catches this and auto-escalates to `prompt=login` so the user
 * does not have to click anything.
 */
export class InteractiveLoginRequiredError extends Error {
  public readonly code: string;
  constructor(code: string) {
    super(code);
    this.name = 'InteractiveLoginRequiredError';
    this.code = code;
  }
}

/**
 * Thrown when the user actively cancelled the Microsoft login flow (clicked
 * "Cancel" or "Tilbage" in the Microsoft dialog, denied consent, etc.). This
 * is NOT a silent-block scenario — the user made a deliberate choice — so the
 * Login page must NOT auto-escalate. It should clear the in-flight reauth
 * flag, clear the PKCE state, and show a friendly message letting the user
 * try again when ready.
 */
export class LoginCancelledError extends Error {
  constructor() {
    super('Brugeren afbrød Microsoft login.');
    this.name = 'LoginCancelledError';
  }
}

const SILENT_REAUTH_REQUIRED_ERRORS = new Set([
  'interaction_required',
  'login_required',
  'consent_required',
  'account_selection_required',
]);

const isSilentBlockedError = (code: string | null | undefined): code is string =>
  !!code && SILENT_REAUTH_REQUIRED_ERRORS.has(code);

/**
 * OAuth-standard error codes that mean the user actively cancelled the
 * Microsoft flow rather than the flow failing silently. `access_denied` is
 * returned when the user clicks "Cancel" / browser-back from the Microsoft
 * sign-in page; `consent_denied` is returned when they decline a consent
 * prompt. Neither should trigger auto-escalation.
 */
const USER_CANCELLED_ERRORS = new Set(['access_denied', 'consent_denied']);

const isUserCancelledError = (code: string | null | undefined): code is string =>
  !!code && USER_CANCELLED_ERRORS.has(code);

/**
 * Microsoft often surfaces "can't do this silently" failures as a free-form
 * `error_description` that starts with `AADSTS<NNNN>:` instead of as an OAuth
 * `error` code (e.g. AADSTS16000 "either multiple user identities or selected
 * account is not supported"). These all mean the same thing for our reauth
 * flow: the browser cannot complete a silent PKCE exchange, so the Login
 * page must auto-escalate to `prompt=login`.
 *
 * Family `16xxx` = user/account selection issues (16000, 16001, 16002, ...).
 * Family `50xxx` = user-interaction required (50020, 50079, 50097, ...).
 * Family `65xxx` = MFA / conditional access.
 *
 * Microsoft never returns `AADSTS*` codes for fatal client-config errors
 * (those come back as standard OAuth `invalid_client`/`invalid_grant` codes
 * with a separate description), so a blanket match is safe here.
 */
const INTERACTIVE_AADSTS_PATTERN = /^AADSTS(1[6-9]\d{3}|5\d{4}|6[5-9]\d{3}):/i;

const isInteractiveAadstsError = (description: string | null | undefined): boolean =>
  !!description && INTERACTIVE_AADSTS_PATTERN.test(description);

export const hasEntraLoginCallback = () => {
  const params = new URLSearchParams(window.location.search);
  return params.has('code') || params.has('error');
};

export const startEntraLogin = async (options: StartEntraLoginOptions = {}) => {
  const config = getOAuthConfig();
  const codeVerifier = randomUrlSafe(64);
  const codeChallenge = await sha256Base64Url(codeVerifier);
  const state = randomUrlSafe(32);
  const redirectUri = config.redirectUri || `${window.location.origin}/login`;
  const returnTo = options.returnTo ?? '/app';

  const pkce: PkceState = { state, codeVerifier, redirectUri, returnTo };
  sessionStorage.setItem(PKCE_KEY, JSON.stringify(pkce));

  const authorizeUrl = new URL(`https://login.microsoftonline.com/${config.tenantId}/oauth2/v2.0/authorize`);
  authorizeUrl.searchParams.set('client_id', config.clientId);
  authorizeUrl.searchParams.set('response_type', 'code');
  authorizeUrl.searchParams.set('redirect_uri', redirectUri);
  authorizeUrl.searchParams.set('response_mode', 'query');
  authorizeUrl.searchParams.set('scope', config.scope);
  authorizeUrl.searchParams.set('state', state);
  authorizeUrl.searchParams.set('code_challenge', codeChallenge);
  authorizeUrl.searchParams.set('code_challenge_method', 'S256');
  authorizeUrl.searchParams.set('prompt', options.prompt ?? 'select_account');

  window.location.assign(authorizeUrl.toString());
};

export const completeEntraLogin = async (): Promise<CompleteEntraLoginResult> => {
  const pkce = loadPkceState();
  const params = new URLSearchParams(window.location.search);
  const error = params.get('error');
  if (error) {
    // Microsoft may redirect back with `error=interaction_required` even from
    // `prompt=none` — that means the user has no SSO cookie. Throw a typed
    // error so Login.tsx can auto-escalate to `prompt=login`.
    if (isSilentBlockedError(error)) {
      throw new InteractiveLoginRequiredError(error);
    }
    // Some failures (e.g. AADSTS16000 "multiple user identities") come back as
    // a free-form `error_description` rather than an OAuth-standard `error`
    // code. Treat those the same way: auto-escalate to interactive login.
    const errorDescription = params.get('error_description');
    if (isInteractiveAadstsError(errorDescription)) {
      throw new InteractiveLoginRequiredError(errorDescription ?? error);
    }
    // User actively cancelled (clicked "Cancel" / "Tilbage" in Microsoft, or
    // denied consent). Do NOT auto-escalate — Login.tsx will show a friendly
    // message and let the user retry on their own terms.
    if (isUserCancelledError(error)) {
      throw new LoginCancelledError();
    }
    throw new Error(errorDescription || error);
  }

  const code = params.get('code');
  const state = params.get('state');
  if (!pkce || !code || state !== pkce.state) {
    throw new Error('Microsoft login kunne ikke valideres. Prøv igen.');
  }

  const config = getOAuthConfig();
  const entraAccessToken = await exchangeCodeForToken(config, pkce, code);

  const auth = await apiClient.post('/api/auth/entra-login', undefined, {
    headers: {
      Authorization: `Bearer ${entraAccessToken}`,
    },
  }) as unknown as AuthTokenResponse;

  return { auth, returnTo: sanitizeReturnTo(pkce.returnTo) };
};

export const clearEntraLoginSession = () => {
  sessionStorage.removeItem(PKCE_KEY);
};

export const sanitizeReturnTo = (returnTo: string | null | undefined) => {
  if (!returnTo || !returnTo.startsWith('/') || returnTo.startsWith('//') || returnTo.startsWith('/login')) {
    return '/app';
  }

  return returnTo;
};

const loadPkceState = (): PkceState | null => {
  const raw = sessionStorage.getItem(PKCE_KEY);
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as Partial<PkceState>;
    if (
      typeof parsed.state !== 'string' || !parsed.state ||
      typeof parsed.codeVerifier !== 'string' || !parsed.codeVerifier ||
      typeof parsed.redirectUri !== 'string' || !parsed.redirectUri ||
      (parsed.returnTo !== undefined && typeof parsed.returnTo !== 'string')
    ) {
      sessionStorage.removeItem(PKCE_KEY);
      return null;
    }

    return {
      state: parsed.state,
      codeVerifier: parsed.codeVerifier,
      redirectUri: parsed.redirectUri,
      ...(parsed.returnTo !== undefined ? { returnTo: parsed.returnTo } : {}),
    };
  } catch {
    sessionStorage.removeItem(PKCE_KEY);
    return null;
  }
};

const getOAuthConfig = () => {
  const tenantId = import.meta.env.VITE_AZURE_AD_TENANT_ID;
  const clientId = import.meta.env.VITE_AZURE_AD_CLIENT_ID;
  const scope = import.meta.env.VITE_AZURE_AD_SCOPE;
  const redirectUri = import.meta.env.VITE_AZURE_AD_LOGIN_REDIRECT_URI;

  if (!tenantId || !clientId || !scope) {
    throw new Error('Microsoft login mangler konfiguration. Sæt VITE_AZURE_AD_TENANT_ID, VITE_AZURE_AD_CLIENT_ID og VITE_AZURE_AD_SCOPE.');
  }

  return { tenantId, clientId, scope, redirectUri };
};

const exchangeCodeForToken = async (
  config: ReturnType<typeof getOAuthConfig>,
  pkce: PkceState,
  code: string,
): Promise<string> => {
  const body = new URLSearchParams();
  body.set('client_id', config.clientId);
  body.set('scope', config.scope);
  body.set('code', code);
  body.set('redirect_uri', pkce.redirectUri);
  body.set('grant_type', 'authorization_code');
  body.set('code_verifier', pkce.codeVerifier);

  const response = await fetch(`https://login.microsoftonline.com/${config.tenantId}/oauth2/v2.0/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
  });

  const payload = await response.json().catch(() => ({} as Record<string, string>));
  if (!response.ok || !payload.access_token) {
    // Silent token-exchange block (e.g. interaction_required surfaced only at /token)
    // also auto-escalates to interactive login.
    if (isSilentBlockedError(payload.error)) {
      throw new InteractiveLoginRequiredError(payload.error);
    }
    // Same escalation for AADSTS-coded failures (e.g. AADSTS16000) that come
    // back via the /token endpoint with no OAuth `error` code.
    if (isInteractiveAadstsError(payload.error_description)) {
      throw new InteractiveLoginRequiredError(payload.error_description ?? 'aadsts');
    }
    throw new Error(payload.error_description || 'Kunne ikke hente Microsoft token.');
  }

  return payload.access_token as string;
};

const randomUrlSafe = (byteLength: number) => {
  const bytes = new Uint8Array(byteLength);
  crypto.getRandomValues(bytes);
  return base64Url(bytes);
};

const sha256Base64Url = async (value: string) => {
  const data = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest('SHA-256', data);
  return base64Url(new Uint8Array(digest));
};

const base64Url = (bytes: Uint8Array) =>
  btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '');
