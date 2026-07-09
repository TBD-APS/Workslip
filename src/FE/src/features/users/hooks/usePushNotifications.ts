import { useMutation } from '@tanstack/react-query';
import { postApiPushSubscriptions } from '../../../api/generated/push-subscriptions/push-subscriptions';
import type { RegisterPushSubscriptionRequest } from '../../../api/generated/models';

const VAPID_PUBLIC_KEY = import.meta.env.VITE_VAPID_PUBLIC_KEY;

export function usePushNotifications() {
  const mutation = useMutation({
    mutationFn: (request: RegisterPushSubscriptionRequest) =>
      postApiPushSubscriptions(request),
  });

  const register = async () => {
    if (!VAPID_PUBLIC_KEY) {
      console.error('VAPID_PUBLIC_KEY is not defined in environment variables.');
      return;
    }

    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
      console.warn('Push notifications are not supported in this browser.');
      return;
    }

    try {
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();

      if (subscription) {
        console.log('Push subscription already exists.');
        return;
      }

      const permission = await Notification.requestPermission();
      if (permission !== 'granted') {
        console.warn('Notification permission was denied.');
        return;
      }

      const newSubscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: VAPID_PUBLIC_KEY,
      });

      // Use type assertion to access non-standard properties in TS
      const p256dh = btoa(String.fromCharCode(...new Uint8Array((newSubscription as any).getPublicKey())));
      const auth = btoa(String.fromCharCode(...new Uint8Array((newSubscription as any).getAuth())));

      await mutation.mutateAsync({
        endpoint: newSubscription.endpoint,
        keys: {
          p256Dh: p256dh,
          auth,
        },
      });

      console.log('Push subscription registered successfully.');
    } catch (error) {
      console.error('Failed to register push subscription:', error);
      throw error;
    }
  };

  return {
    register,
    isLoading: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  };
}
