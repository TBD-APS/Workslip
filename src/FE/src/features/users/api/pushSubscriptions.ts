import { apiClient } from '../../../lib/axios';

export type RegisterPushSubscriptionPayload = {
  endpoint: string;
  keys: {
    p256Dh: string;
    auth: string;
  };
  replacedEndpoint?: string;
};

type VapidPublicKeyResponse = {
  publicKey: string;
};

export async function getVapidPublicKey(): Promise<string> {
  const response = await apiClient.get<never, VapidPublicKeyResponse>(
    '/api/push-subscriptions/public-key',
    { skipGlobalErrorToast: true },
  );
  return response.publicKey;
}

export function registerPushSubscription(
  payload: RegisterPushSubscriptionPayload,
): Promise<void> {
  return apiClient.post('/api/push-subscriptions', payload, {
    skipGlobalErrorToast: true,
  });
}
