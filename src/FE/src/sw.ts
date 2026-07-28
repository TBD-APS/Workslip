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
const ROUTE_ASSET_CACHE = 'workslip-route-assets-v1';
const MAX_ROUTE_ASSET_ENTRIES = 100;

cleanupOutdatedCaches();
precacheAndRoute(PRECACHE_MANIFEST);

// This remains aligned with registerType: 'autoUpdate'. WOR-114 owns the
// separate change to prompt-based, dirty-form-safe activation.
self.skipWaiting();
clientsClaim();

function isLazyRouteAsset(request: Request) {
  if (request.method !== 'GET') return false;
  if (request.destination !== 'script' && request.destination !== 'style') return false;

  const url = new URL(request.url);
  return url.origin === self.location.origin
    && url.pathname.startsWith('/assets/chunks/')
    && !PRECACHED_URLS.has(url.href);
}

async function trimRouteAssetCache(cache: Cache) {
  const requests = await cache.keys();
  const excessCount = requests.length - MAX_ROUTE_ASSET_ENTRIES;
  if (excessCount <= 0) return;

  await Promise.all(
    requests.slice(0, excessCount).map((request) => cache.delete(request)),
  );
}

self.addEventListener('fetch', (event) => {
  if (!isLazyRouteAsset(event.request)) return;

  event.respondWith((async () => {
    const cache = await caches.open(ROUTE_ASSET_CACHE);
    const cachedResponse = await cache.match(event.request);
    if (cachedResponse) return cachedResponse;

    const response = await fetch(event.request);
    if (response.ok) {
      await cache.put(event.request, response.clone());
      await trimRouteAssetCache(cache);
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