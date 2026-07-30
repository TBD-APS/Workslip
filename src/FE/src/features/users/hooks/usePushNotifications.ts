import { useCallback } from 'react';
import { useMutation } from '@tanstack/react-query';
import {
  getVapidPublicKey,
  registerPushSubscription,
  type RegisterPushSubscriptionPayload,
} from '../api/pushSubscriptions';
import { isSuperadminAuthToken } from '../../superadmin/organizationSession';

function urlBase64ToUint8Array(base64String: string) {
  const padding = '='.repeat((4 - base64String.length % 4) % 4);
  const base64 = (base64String + padding)
    .replace(/-/g, '+')
    .replace(/_/g, '/');
  const rawData = window.atob(base64);
  return Uint8Array.from([...rawData].map((char) => char.charCodeAt(0)));
}

function toUint8Array(value: BufferSource): Uint8Array {
  return value instanceof ArrayBuffer
    ? new Uint8Array(value)
    : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
}

function keysMatch(
  existingKey: BufferSource | null,
  expectedKey: Uint8Array,
): boolean {
  if (!existingKey) return false;

  const existingBytes = toUint8Array(existingKey);
  if (existingBytes.length !== expectedKey.length) return false;
  return existingBytes.every((value, index) => value === expectedKey[index]);
}

async function ensurePushSubscription(): Promise<RegisterPushSubscriptionPayload | null> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
    console.warn('Push notifications are not supported in this browser.');
    return null;
  }

  const publicKey = await getVapidPublicKey();
  const applicationServerKey = urlBase64ToUint8Array(publicKey);
  const registration = await navigator.serviceWorker.ready;
  let subscription = await registration.pushManager.getSubscription();
  let replacedEndpoint: string | undefined;

  if (subscription && !keysMatch(subscription.options.applicationServerKey, applicationServerKey)) {
    replacedEndpoint = subscription.endpoint;
    const unsubscribed = await subscription.unsubscribe();
    if (!unsubscribed) {
      throw new Error('The stale push subscription could not be removed.');
    }
    subscription = null;
  }

  if (!subscription) {
    const permission = Notification.permission === 'default'
      ? await Notification.requestPermission()
      : Notification.permission;
    if (permission !== 'granted') {
      console.warn('Notification permission was denied.');
      return null;
    }

    subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey,
    });
  }

  const rawP256dh = subscription.getKey('p256dh');
  const rawAuth = subscription.getKey('auth');

  if (!rawP256dh || !rawAuth) {
    throw new Error('Could not retrieve keys from subscription object.');
  }

  const p256Dh = btoa(String.fromCharCode(...new Uint8Array(rawP256dh)));
  const auth = btoa(String.fromCharCode(...new Uint8Array(rawAuth)));

  return {
    endpoint: subscription.endpoint,
    keys: {
      p256Dh,
      auth,
    },
    ...(replacedEndpoint ? { replacedEndpoint } : {}),
  };
}

export function usePushNotifications() {
  const mutation = useMutation({
    mutationFn: registerPushSubscription,
  });

  const register = useCallback(async () => {
    // Platform operators retain their own identity while changing effective
    // organization. Never subscribe that device to a tenant notification feed.
    if (isSuperadminAuthToken()) {
      return;
    }

    try {
      const payload = await ensurePushSubscription();
      if (!payload) return;

      await mutation.mutateAsync(payload);
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

export const pushSubscriptionInternals = {
  ensurePushSubscription,
  keysMatch,
  urlBase64ToUint8Array,
};
