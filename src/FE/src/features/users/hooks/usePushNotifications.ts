import { useMutation } from '@tanstack/react-query';
import { postApiPushSubscriptions } from '../../../api/generated/push-subscriptions/push-subscriptions';
import type { RegisterPushSubscriptionRequest } from '../../../api/generated/models';

function urlBase64ToUint8Array(base64String: string): Uint8Array {
  // 1. Fjern eventuelle usynlige linjeskift, mellemrum eller gåseøjne
  let cleaned = base64String.trim().replace(/["']/g, '').replace(/\s/g, '');

  // 2. Konverter fra Base64Url til standard Base64 (hvis det ikke allerede er det)
  cleaned = cleaned.replace(/\-/g, '+').replace(/_/g, '/');

  // 3. Håndter padding (skal gå op i 4)
  const pad = (4 - (cleaned.length % 4)) % 4;
  if (pad > 0) {
    cleaned += '='.repeat(pad);
  }

  // 4. Afkod strengen til binær data
  const rawData = window.atob(cleaned);
  const outputArray = new Uint8Array(rawData.length);

  for (let i = 0; i < rawData.length; ++i) {
    outputArray[i] = rawData.charCodeAt(i);
  }

  return outputArray;
}

const VAPID_PUBLIC_KEY = import.meta.env.VITE_VAPID_PUBLIC_KEY;
// Konverter nøglen ÉN gang herude, hvis den eksisterer
const VAPID_PUBLIC_KEY_ARRAY = urlBase64ToUint8Array(VAPID_PUBLIC_KEY);

export function usePushNotifications() {
  const mutation = useMutation({
    mutationFn: (request: RegisterPushSubscriptionRequest) =>
      postApiPushSubscriptions(request),
  });

  const register = async () => {
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

      // Send det binære array med i stedet for strengen
      const newSubscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: VAPID_PUBLIC_KEY_ARRAY.buffer as ArrayBuffer,
      });

      // 2. Moderne og standardiseret måde at hente p256dh og auth på (uden non-standard casting)
      const rawP256dh = newSubscription.getKey('p256dh');
      const rawAuth = newSubscription.getKey('auth');

      if (!rawP256dh || !rawAuth) {
        throw new Error('Could not retrieve keys from subscription object.');
      }

      const p256dh = btoa(String.fromCharCode(...new Uint8Array(rawP256dh)));
      const auth = btoa(String.fromCharCode(...new Uint8Array(rawAuth)));

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