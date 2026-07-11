// @ts-nocheck — service worker types not in app tsconfig; built by Vite only
import { clientsClaim } from 'workbox-core';
import { precacheAndRoute } from 'workbox-precaching';

declare const self: ServiceWorkerGlobalScope;

precacheAndRoute(self.__WB_MANIFEST);

self.skipWaiting();
clientsClaim();

self.addEventListener('push', (event) => {
  console.log('[SW] Push event received', event.data);
  if (!event.data) {
    console.warn('[SW] Push event has no data — showing fallback notification');
    event.waitUntil(
      self.registration.showNotification('Workslip', {
        body: 'You have a new notification',
        icon: '/logo.png',
        badge: '/logo.png',
      })
    );
    return;
  }

  let payload;
  try {
    payload = event.data.json();
  } catch (e) {
    console.error('Failed to parse push payload:', e);
    return;
  }

  const title = payload.title || 'Workslip';
  const options = payload.options || {};

  event.waitUntil(
    self.registration.showNotification(title, {
      body: options.body || '',
      icon: options.icon || '/logo.png',
      badge: options.badge || '/logo.png',
      tag: options.tag || '',
      data: options.data || {},
    }).catch((err) => {
      console.error('[SW] showNotification failed:', err);
    })
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const urlToOpen = event.notification.data?.url || '/';

  event.waitUntil(
    self.clients
      .matchAll({ type: 'window', includeUncontrolled: true })
      .then((windowClients) => {
        for (const client of windowClients) {
          if ('focus' in client && client.url === urlToOpen) {
            return client.focus();
          }
        }
        if (self.clients.openWindow) {
          return self.clients.openWindow(urlToOpen);
        }
      })
  );
});
