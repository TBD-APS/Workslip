import { makeRedirectUri } from 'expo-auth-session';

export const azureConfig = {
  tenantId: process.env.EXPO_PUBLIC_AZURE_TENANT_ID ?? '',
  clientId: process.env.EXPO_PUBLIC_AZURE_CLIENT_ID ?? '',
};

export const scopes = [
  'openid',
  'profile',
  'email',
  'offline_access',
];

export const redirectUri = makeRedirectUri({
  scheme: 'nativepasskeydemo',
  path: 'auth',
});

export const getDiscoveryUrl = (tenantId: string) =>
  `https://login.microsoftonline.com/${tenantId}/v2.0`;
