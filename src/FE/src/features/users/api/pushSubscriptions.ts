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

export async function getVapidPublicKey(): Promise<string | null> {
  try {
    const response = await apiClient.get<never, VapidPublicKeyResponse | null>(
      '/api/push-subscriptions/public-key',
      {
        skipGlobalErrorToast: true,
        validateStatus: (status) => (status >= 200 && status < 300) || status === 204,
      },
    );
    const publicKey = (response as VapidPublicKeyResponse | null)?.publicKey?.trim();
    return publicKey ? publicKey : null;
  } catch {
    // 204 No Content (push disabled) or network error – treat as not configured
    return null;
  }
}

export function registerPushSubscription(
  payload: RegisterPushSubscriptionPayload,
): Promise<void> {
  return apiClient.post('/api/push-subscriptions', payload, {
    skipGlobalErrorToast: true,
  });
}
