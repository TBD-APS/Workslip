import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching';
import {
  isNotificationNavigationAcknowledgement,
  navigateNotificationTarget,
  NOTIFICATION_NAVIGATION_REQUEST,
  NOTIFICATION_RECEIVED,
  type NotificationReceivedMessage,
  type NotificationWindowClient,
} from './pwa/notificationNavigation';

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
const CLIENT_NAVIGATION_TIMEOUT_MS = 1_500;

cleanupOutdatedCaches();
precacheAndRoute(PRECACHE_MANIFEST);

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});

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

async function notifyOpenClientsOfPush(): Promise<void> {
  const windowClients = await self.clients.matchAll({
    type: 'window',
    includeUncontrolled: true,
  });

  const message: NotificationReceivedMessage = { type: NOTIFICATION_RECEIVED };
  for (const client of windowClients) {
    try {
      client.postMessage(message);
    } catch {
      // The client may have been closed or is otherwise not deliverable.
    }
  }
}

self.addEventListener('push', (event) => {
  console.log('[SW] Push event received', event.data);
  if (!event.data) {
    console.warn('[SW] Push event has no data — showing fallback notification');
    event.waitUntil(
      (async () => {
        await self.registration.showNotification('Workslip', {
          body: 'You have a new notification',
          icon: '/logo.png',
          badge: '/logo.png',
        });
        await notifyOpenClientsOfPush();
      })().catch((error) => {
        console.error('[SW] Fallback notification failed:', error);
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
    (async () => {
      await self.registration.showNotification(title, {
        body: options.body || '',
        icon: options.icon || '/logo.png',
        badge: options.badge || '/logo.png',
        tag: options.tag || '',
        data: options.data || {},
      });
      await notifyOpenClientsOfPush();
    })().catch((error) => {
      console.error('[SW] showNotification failed:', error);
    })
  );
});

async function requestClientRouterNavigation(
  client: NotificationWindowClient,
  url: string,
): Promise<boolean> {
  const channel = new MessageChannel();

  return new Promise((resolve) => {
    let settled = false;
    const finish = (handled: boolean) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      channel.port1.close();
      resolve(handled);
    };
    const timeout = setTimeout(() => finish(false), CLIENT_NAVIGATION_TIMEOUT_MS);

    channel.port1.onmessage = (event) => {
      const acknowledgement = event.data;
      finish(
        isNotificationNavigationAcknowledgement(acknowledgement)
        && acknowledgement.success,
      );
    };

    try {
      (client as WindowClient).postMessage({
        type: NOTIFICATION_NAVIGATION_REQUEST,
        url,
      }, [channel.port2]);
    } catch {
      finish(false);
    }
  });
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  event.waitUntil((async () => {
    const windowClients = await self.clients.matchAll({
      type: 'window',
      includeUncontrolled: true,
    });

    await navigateNotificationTarget(
      windowClients,
      (url) => self.clients.openWindow(url),
      event.notification.data?.url,
      self.location.origin,
      requestClientRouterNavigation,
    );
  })());
});
