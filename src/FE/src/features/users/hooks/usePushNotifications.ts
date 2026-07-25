import { useCallback } from 'react';
import { useMutation } from '@tanstack/react-query';
import { postApiPushSubscriptions } from '../../../api/generated/push-subscriptions/push-subscriptions';
import type { RegisterPushSubscriptionRequest } from '../../../api/generated/models';

function urlBase64ToUint8Array(base64String: string) {
  const padding = '='.repeat((4 - base64String.length % 4) % 4);
  const base64 = (base64String + padding)
    .replace(/-/g, '+')
    .replace(/_/g, '/');
  const rawData = window.atob(base64);
  return Uint8Array.from([...rawData].map((char) => char.charCodeAt(0)));
}

const VAPID_PUBLIC_KEY = import.meta.env.VITE_VAPID_PUBLIC_KEY as string | undefined;
const VAPID_PUBLIC_KEY_ARRAY = VAPID_PUBLIC_KEY
  ? urlBase64ToUint8Array(VAPID_PUBLIC_KEY)
  : null;

export function usePushNotifications() {
  const mutation = useMutation({
    mutationFn: (request: RegisterPushSubscriptionRequest) =>
      postApiPushSubscriptions(request),
  });

  const register = useCallback(async () => {
    if (!VAPID_PUBLIC_KEY_ARRAY) {
      console.error('VAPID_PUBLIC_KEY is not defined or invalid in environment variables.');
      return;
    }

    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
      console.warn('Push notifications are not supported in this browser.');
      return;
    }

    try {
      const registration = await navigator.serviceWorker.ready;
      let subscription = await registration.pushManager.getSubscription();

      if (!subscription) {
        const permission = await Notification.requestPermission();
        if (permission !== 'granted') {
          console.warn('Notification permission was denied.');
          return;
        }

        subscription = await registration.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: VAPID_PUBLIC_KEY_ARRAY,
        });
      }

      const rawP256dh = subscription.getKey('p256dh');
      const rawAuth = subscription.getKey('auth');

      if (!rawP256dh || !rawAuth) {
        throw new Error('Could not retrieve keys from subscription object.');
      }

      const p256dh = btoa(String.fromCharCode(...new Uint8Array(rawP256dh)));
      const auth = btoa(String.fromCharCode(...new Uint8Array(rawAuth)));

      await mutation.mutateAsync({
        endpoint: subscription.endpoint,
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
  }, [mutation]);

  return {
    register,
    isLoading: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  };
}
