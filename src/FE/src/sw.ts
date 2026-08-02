import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching';
import {
  isNotificationNavigationAcknowledgement,
  navigateNotificationTarget,
  NOTIFICATION_NAVIGATION_REQUEST,
  NOTIFICATION_RECEIVED,
  type NotificationReceivedMessage,
  type NotificationWindowClient,
} from './pwa/notificationNavigation';
import { normalizePushNotificationPayload } from './pwa/pushNotificationPayload';

type PrecacheManifestEntry = string | {
  url: string;
  revision?: string | null;
};

declare const self: ServiceWorkerGlobalScope & {
  __WB_MANIFEST: PrecacheManifestEntry[];
};

interface RuntimeAssetResult {
  response: Response;
  cacheWork: Promise<void>;
}

const PRECACHE_MANIFEST = self.__WB_MANIFEST;
const PRECACHED_URLS = new Set(
  PRECACHE_MANIFEST.map((entry) =>
    new URL(typeof entry === 'string' ? entry : entry.url, self.location.origin).href,
  ),
);
const RUNTIME_ASSET_CACHE = 'workslip-route-assets-v1';
const MAX_RUNTIME_ASSET_ENTRIES = 150;
const RUNTIME_CACHE_TRIM_WRITE_INTERVAL = 10;
const CLIENT_NAVIGATION_TIMEOUT_MS = 1_500;

let runtimeAssetWritesSinceTrim = 0;
let runtimeCacheTrimPromise: Promise<void> | null = null;

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

function scheduleRuntimeAssetCacheTrim(cache: Cache): Promise<void> {
  runtimeAssetWritesSinceTrim += 1;
  if (runtimeAssetWritesSinceTrim < RUNTIME_CACHE_TRIM_WRITE_INTERVAL) {
    return Promise.resolve();
  }

  if (runtimeCacheTrimPromise) return runtimeCacheTrimPromise;

  const trimPromise = (async () => {
    while (runtimeAssetWritesSinceTrim >= RUNTIME_CACHE_TRIM_WRITE_INTERVAL) {
      runtimeAssetWritesSinceTrim = 0;
      await trimRuntimeAssetCache(cache);
    }
  })()
    .catch((error) => {
      console.warn('[SW] Runtime asset cache trim failed:', error);
    })
    .finally(() => {
      if (runtimeCacheTrimPromise === trimPromise) {
        runtimeCacheTrimPromise = null;
      }
    });

  runtimeCacheTrimPromise = trimPromise;
  return trimPromise;
}

async function cacheRuntimeAsset(
  cache: Cache,
  request: Request,
  response: Response,
): Promise<void> {
  await cache.put(request, response);
  await scheduleRuntimeAssetCacheTrim(cache);
}

async function prepareRuntimeAssetResponse(request: Request): Promise<RuntimeAssetResult> {
  let cache: Cache;

  try {
    cache = await caches.open(RUNTIME_ASSET_CACHE);
    const cachedResponse = await cache.match(request);
    if (cachedResponse) {
      return {
        response: cachedResponse,
        cacheWork: Promise.resolve(),
      };
    }
  } catch (error) {
    console.warn('[SW] Runtime asset cache lookup failed; using network:', error);
    return {
      response: await fetch(request),
      cacheWork: Promise.resolve(),
    };
  }

  const response = await fetch(request);
  const cacheWork = response.ok
    ? cacheRuntimeAsset(cache, request, response.clone()).catch((error) => {
        console.warn('[SW] Runtime asset cache write failed:', error);
      })
    : Promise.resolve();

  return { response, cacheWork };
}

self.addEventListener('fetch', (event) => {
  if (!isRuntimeStaticAsset(event.request)) return;

  const operation = prepareRuntimeAssetResponse(event.request);

  event.respondWith(operation.then(({ response }) => response));
  event.waitUntil(
    operation
      .then(({ cacheWork }) => cacheWork)
      .catch(() => {
        // The response path owns network failures. Cache maintenance must never
        // turn a recoverable asset request into an additional worker rejection.
      }),
  );
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

function readPushPayload(event: PushEvent): unknown {
  if (!event.data) return undefined;

  try {
    return event.data.json() as unknown;
  } catch (error) {
    console.error('[SW] Failed to parse push payload; using fallback:', error);
    return undefined;
  }
}

self.addEventListener('push', (event) => {
  const payload = normalizePushNotificationPayload(readPushPayload(event));

  event.waitUntil(
    (async () => {
      await self.registration.showNotification(payload.title, payload.options);
      await notifyOpenClientsOfPush();
    })().catch((error) => {
      console.error('[SW] showNotification failed:', error);
    }),
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
