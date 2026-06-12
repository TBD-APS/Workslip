import { apiClient } from '../../../lib/axios';
import type { AuthTokenResponse } from '../../../api/generated/models';

const ENROLLMENT_KEY = 'workslip.inviteEnrollment';
const PKCE_KEY = 'workslip.invitePkce';

export interface InviteEnrollmentDraft {
  token: string;
  displayName: string;
  phone?: string;
}

interface PkceState {
  state: string;
  codeVerifier: string;
  redirectUri: string;
}

export const saveInviteEnrollmentDraft = (draft: InviteEnrollmentDraft) => {
  sessionStorage.setItem(ENROLLMENT_KEY, JSON.stringify(draft));
};

export const loadInviteEnrollmentDraft = (): InviteEnrollmentDraft | null => {
  const raw = sessionStorage.getItem(ENROLLMENT_KEY);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as InviteEnrollmentDraft;
  } catch {
    return null;
  }
};

export const clearInviteEnrollmentSession = () => {
  sessionStorage.removeItem(ENROLLMENT_KEY);
  sessionStorage.removeItem(PKCE_KEY);
};

export const hasEntraCallback = () => {
  const params = new URLSearchParams(window.location.search);
  return params.has('code') || params.has('error');
};

export const startEntraInviteSignIn = async (draft: InviteEnrollmentDraft) => {
  saveInviteEnrollmentDraft(draft);

  const config = getOAuthConfig();
  const codeVerifier = randomUrlSafe(64);
  const codeChallenge = await sha256Base64Url(codeVerifier);
  const state = randomUrlSafe(32);
  const redirectUri = config.redirectUri || `${window.location.origin}${window.location.pathname}`;

  const pkce: PkceState = { state, codeVerifier, redirectUri };
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
  authorizeUrl.searchParams.set('prompt', 'select_account');

  window.location.assign(authorizeUrl.toString());
};

export const completeEntraInviteEnrollment = async (): Promise<AuthTokenResponse> => {
  const draft = loadInviteEnrollmentDraft();
  const pkce = loadPkceState();
  const params = new URLSearchParams(window.location.search);
  const error = params.get('error');
  if (error) {
    throw new Error(params.get('error_description') || error);
  }

  const code = params.get('code');
  const state = params.get('state');
  if (!draft || !pkce || !code || state !== pkce.state) {
    throw new Error('Microsoft login kunne ikke valideres. Start invitationen igen.');
  }

  const config = getOAuthConfig();
  const entraAccessToken = await exchangeCodeForToken(config, pkce, code);

  return apiClient.post('/api/auth/entra-enroll', {
    token: draft.token,
    displayName: draft.displayName,
    phone: draft.phone,
  }, {
    headers: {
      Authorization: `Bearer ${entraAccessToken}`,
    },
  });
};

const loadPkceState = (): PkceState | null => {
  const raw = sessionStorage.getItem(PKCE_KEY);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as PkceState;
  } catch {
    return null;
  }
};

const getOAuthConfig = () => {
  const tenantId = import.meta.env.VITE_AZURE_AD_TENANT_ID;
  const clientId = import.meta.env.VITE_AZURE_AD_CLIENT_ID;
  const scope = import.meta.env.VITE_AZURE_AD_SCOPE;
  const redirectUri = import.meta.env.VITE_AZURE_AD_REDIRECT_URI;

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

  const payload = await response.json();
  if (!response.ok || !payload.access_token) {
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
