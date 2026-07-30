/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';
import {
  announcePwaUpdateApplying,
  announcePwaUpdateReady,
  PWA_UPDATE_APPLY_EVENT,
} from './lib/pwaUpdateEvents';

const UPDATE_INTERVAL_MS = 60 * 1000;
const UPDATE_ACTIVATION_GRACE_MS = 10_000;
const UPDATE_RELOAD_FALLBACK_MS = 5_000;
const SKIP_WAITING_MESSAGE = { type: 'SKIP_WAITING' } as const;

const serviceWorkerSupported = 'serviceWorker' in navigator;
const hadControllerAtStartup = serviceWorkerSupported
  && Boolean(navigator.serviceWorker.controller);

let reloadRequested = false;
let reloadFallback: number | undefined;
let automaticUpdateTimer: number | undefined;
let updateAvailable = false;
let updateApplying = false;
let serviceWorkerRegistration: ServiceWorkerRegistration | null = null;
let requestRegisteredUpdate: (() => void) | null = null;

function clearReloadFallback() {
  if (reloadFallback === undefined) return;

  window.clearTimeout(reloadFallback);
  reloadFallback = undefined;
}

function clearAutomaticUpdateTimer() {
  if (automaticUpdateTimer === undefined) return;

  window.clearTimeout(automaticUpdateTimer);
  automaticUpdateTimer = undefined;
}

function reloadForUpdate() {
  if (!hadControllerAtStartup || reloadRequested) return;

  reloadRequested = true;
  clearAutomaticUpdateTimer();
  clearReloadFallback();
  window.location.reload();
}

function scheduleReloadFallback() {
  if (!hadControllerAtStartup || reloadRequested || reloadFallback !== undefined) return;

  reloadFallback = window.setTimeout(reloadForUpdate, UPDATE_RELOAD_FALLBACK_MS);
}

function announceUpdateAvailable() {
  if (
    !hadControllerAtStartup
    || updateApplying
    || !serviceWorkerRegistration?.waiting
  ) return;

  updateAvailable = true;
  announcePwaUpdateReady();

  if (automaticUpdateTimer !== undefined) return;

  automaticUpdateTimer = window.setTimeout(() => {
    automaticUpdateTimer = undefined;
    applyAvailableUpdate();
  }, UPDATE_ACTIVATION_GRACE_MS);
}

async function resolveRegistrationAndAnnounceUpdate() {
  if (!serviceWorkerSupported || updateApplying) return;

  const resolvedRegistration = serviceWorkerRegistration
    ?? await navigator.serviceWorker.getRegistration();
  serviceWorkerRegistration = resolvedRegistration ?? null;
  announceUpdateAvailable();
}

function recoverFromActivationFailure(error: unknown) {
  updateApplying = false;
  clearReloadFallback();
  console.error('[PWA] Failed to activate update:', error);

  updateAvailable = Boolean(serviceWorkerRegistration?.waiting);
  if (updateAvailable) {
    announceUpdateAvailable();
  } else {
    requestRegisteredUpdate?.();
  }
}

function applyAvailableUpdate() {
  if (!hadControllerAtStartup || updateApplying) return;

  const waitingWorker = serviceWorkerRegistration?.waiting;
  if (!updateAvailable || !waitingWorker) {
    updateAvailable = false;
    requestRegisteredUpdate?.();
    return;
  }

  updateAvailable = false;
  updateApplying = true;
  clearAutomaticUpdateTimer();
  announcePwaUpdateApplying();
  scheduleReloadFallback();

  try {
    waitingWorker.postMessage(SKIP_WAITING_MESSAGE);
  } catch (error) {
    recoverFromActivationFailure(error);
  }
}

async function checkForServiceWorkerUpdate(
  swUrl: string,
  registration: ServiceWorkerRegistration,
) {
  if (
    !navigator.onLine
    || registration.installing
    || registration.waiting
    || updateApplying
  ) return;

  try {
    const response = await fetch(swUrl, {
      cache: 'no-store',
      headers: {
        'cache': 'no-store',
        'cache-control': 'no-cache',
      },
    });

    if (response.status === 200) {
      await registration.update();
    }
  } catch {
    // Startup, visibility, online and interval checks all retry this path.
  }
}

if (serviceWorkerSupported) {
  // This is the single normal page-reload owner. The custom worker only claims
  // clients; it never navigates them itself.
  navigator.serviceWorker.addEventListener('controllerchange', reloadForUpdate);
}

window.addEventListener(PWA_UPDATE_APPLY_EVENT, applyAvailableUpdate);

registerSW({
  immediate: true,
  onNeedRefresh() {
    void resolveRegistrationAndAnnounceUpdate();
  },
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    serviceWorkerRegistration = registration;

    let updateCheck: Promise<void> | null = null;
    const requestUpdate = () => {
      if (updateCheck) return;

      updateCheck = checkForServiceWorkerUpdate(swUrl, registration)
        .finally(() => {
          updateCheck = null;
        });
    };

    requestRegisteredUpdate = requestUpdate;

    if (registration.waiting) {
      announceUpdateAvailable();
    }

    // Discover a deployment immediately when the app starts, returns to the
    // foreground or regains connectivity, and at most one minute afterwards.
    requestUpdate();
    window.setInterval(requestUpdate, UPDATE_INTERVAL_MS);
    window.addEventListener('online', requestUpdate);

    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') {
        requestUpdate();
      }
    });
  },
  onRegisterError(error) {
    console.error('[PWA] Registration failed:', error);
  },
});
