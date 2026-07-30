import { clientsClaim } from 'workbox-core';
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching';

type PrecacheManifestEntry = string | {
  url: string;
  revision?: string | null;
};

declare const self: ServiceWorkerGlobalScope & {
  __WB_MANIFEST: PrecacheManifestEntry[];
};

const PRECACHE_MANIFEST = self.__WB_MANIFEST;
const PRECACHED_URLS = new Set(
  PRECACHE_MANIFEST.map((entry) =>
    new URL(typeof entry === 'string' ? entry : entry.url, self.location.origin).href,
  ),
);
const RUNTIME_ASSET_CACHE = 'workslip-route-assets-v1';
const MAX_RUNTIME_ASSET_ENTRIES = 150;

cleanupOutdatedCaches();
precacheAndRoute(PRECACHE_MANIFEST);

// Immediate activation is the accepted product policy in ADR 0002.
self.skipWaiting();
clientsClaim();

self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') {
    event.waitUntil(self.skipWaiting());
  }
});

function isRuntimeStaticAsset(request: Request) {
  if (request.method !== 'GET') return false;
  if (!['script', 'style', 'font', 'image'].includes(request.destination)) return false;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin || PRECACHED_URLS.has(url.href)) return false;

  return url.pathname.startsWith('/assets/') || url.pathname.startsWith('/fonts/');
}

async function trimRuntimeAssetCache(cache: Cache) {
  const requests = await cache.keys();
  const excessCount = requests.length - MAX_RUNTIME_ASSET_ENTRIES;
  if (excessCount <= 0) return;

  await Promise.all(
    requests.slice(0, excessCount).map((request) => cache.delete(request)),
  );
}

self.addEventListener('fetch', (event) => {
  if (!isRuntimeStaticAsset(event.request)) return;

  event.respondWith((async () => {
    const cache = await caches.open(RUNTIME_ASSET_CACHE);
    const cachedResponse = await cache.match(event.request);
    if (cachedResponse) return cachedResponse;

    const response = await fetch(event.request);
    if (response.ok) {
      await cache.put(event.request, response.clone());
      await trimRuntimeAssetCache(cache);
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
  } catch (error) {
    console.error('Failed to parse push payload:', error);
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
    }).catch((error) => {
      console.error('[SW] showNotification failed:', error);
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
