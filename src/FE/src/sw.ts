// @ts-nocheck — service worker types not in app tsconfig; built by Vite only
import { precacheAndRoute } from 'workbox-precaching';

declare const self: ServiceWorkerGlobalScope;
declare const __BUILD_TIME__: string;

const PRECACHE_MANIFEST = self.__WB_MANIFEST;
const PRECACHED_URLS = new Set(
  PRECACHE_MANIFEST.map((entry) =>
    new URL(typeof entry === 'string' ? entry : entry.url, self.location.origin).href,
  ),
);
const BUILD_TIME = __BUILD_TIME__;
const ROUTE_ASSET_CACHE_PREFIX = 'workslip-route-assets-';
const ROUTE_ASSET_CACHE = `${ROUTE_ASSET_CACHE_PREFIX}${BUILD_TIME}`;

precacheAndRoute(PRECACHE_MANIFEST);

function isLazyRouteAsset(request: Request) {
  if (request.method !== 'GET') return false;
  if (request.destination !== 'script' && request.destination !== 'style') return false;

  const url = new URL(request.url);
  return url.origin === self.location.origin
    && url.pathname.startsWith('/assets/')
    && !PRECACHED_URLS.has(url.href);
}

async function deleteOldRouteAssetCaches() {
  const cacheNames = await caches.keys();
  await Promise.all(
    cacheNames
      .filter((cacheName) => cacheName.startsWith(ROUTE_ASSET_CACHE_PREFIX) && cacheName !== ROUTE_ASSET_CACHE)
      .map((cacheName) => caches.delete(cacheName)),
  );
}

self.addEventListener('install', () => {
  console.log('[SW] Installing (build:', BUILD_TIME + ')');
  self.skipWaiting();
});

self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') {
    console.log('[SW] SKIP_WAITING received — activating');
    self.skipWaiting();
  }
});

self.addEventListener('activate', (event) => {
  console.log('[SW] Activating (build:', BUILD_TIME + ')');
  event.waitUntil((async () => {
    await Promise.all([
      self.clients.claim(),
      deleteOldRouteAssetCaches(),
    ]);

    const clients = await self.clients.matchAll({ type: 'window' });
    for (const client of clients) {
      client.postMessage({ type: 'RELOAD' });
    }
  })());
});

self.addEventListener('fetch', (event) => {
  if (!isLazyRouteAsset(event.request)) return;

  event.respondWith((async () => {
    const cache = await caches.open(ROUTE_ASSET_CACHE);
    const cachedResponse = await cache.match(event.request);
    if (cachedResponse) return cachedResponse;

    const response = await fetch(event.request);
    if (response.ok) {
      await cache.put(event.request, response.clone());
    }

    return response;
  })());
});

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
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windowClients) => {
      const existingClient = windowClients[0];
      if (existingClient) {
        existingClient.navigate(urlToOpen);
        return existingClient.focus();
      }
      if (self.clients.openWindow) {
        return self.clients.openWindow(urlToOpen);
      }
    })
  );
});