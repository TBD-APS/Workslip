/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';
import {
  announcePwaUpdateApplying,
  announcePwaUpdateReady,
  PWA_UPDATE_APPLY_EVENT,
} from './lib/pwaUpdateEvents';

const UPDATE_INTERVAL_MS = 60 * 1000;
const UPDATE_ACTIVATION_GRACE_MS = 10_000;
const UPDATE_RELOAD_FALLBACK_MS = 2_000;
const CONFIRMED_RELOAD_GUARD_MS = 10_000;
const CONFIRMED_RELOAD_AT_KEY = 'workslip.pwaUpdateReloadAt';
const FALLBACK_RELOAD_KEY = `workslip.pwaUpdateFallback:${__BUILD_TIME__}`;

const serviceWorkerSupported = 'serviceWorker' in navigator;
const hadControllerAtStartup = serviceWorkerSupported
  && Boolean(navigator.serviceWorker.controller);

let reloadRequested = false;
let reloadFallback: number | undefined;
let automaticUpdateTimer: number | undefined;
let updateAvailable = false;
let updateApplying = false;
let updateServiceWorker: ReturnType<typeof registerSW> | null = null;

function readSessionValue(key: string) {
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeSessionValue(key: string, value: string) {
  try {
    window.sessionStorage.setItem(key, value);
  } catch {
    // The in-memory guard still prevents duplicate reloads in this document.
  }
}

function wasConfirmedReloadRecent() {
  const lastReloadAt = Number(readSessionValue(CONFIRMED_RELOAD_AT_KEY));
  return Number.isFinite(lastReloadAt)
    && Date.now() - lastReloadAt < CONFIRMED_RELOAD_GUARD_MS;
}

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
  if (!hadControllerAtStartup || reloadRequested || wasConfirmedReloadRecent()) return;

  reloadRequested = true;
  updateApplying = true;
  announcePwaUpdateApplying();
  writeSessionValue(CONFIRMED_RELOAD_AT_KEY, Date.now().toString());
  clearAutomaticUpdateTimer();
  clearReloadFallback();
  window.location.reload();
}

function reloadFromFallback() {
  reloadFallback = undefined;
  if (!hadControllerAtStartup || reloadRequested || readSessionValue(FALLBACK_RELOAD_KEY)) return;

  reloadRequested = true;
  updateApplying = true;
  announcePwaUpdateApplying();
  writeSessionValue(FALLBACK_RELOAD_KEY, '1');
  clearAutomaticUpdateTimer();
  window.location.reload();
}

function scheduleReloadFallback() {
  if (
    !hadControllerAtStartup
    || reloadRequested
    || reloadFallback !== undefined
    || readSessionValue(FALLBACK_RELOAD_KEY)
  ) return;

  reloadFallback = window.setTimeout(reloadFromFallback, UPDATE_RELOAD_FALLBACK_MS);
}

function announceUpdateAvailable() {
  if (!hadControllerAtStartup || updateApplying) return;

  updateAvailable = true;
  announcePwaUpdateReady();

  if (automaticUpdateTimer !== undefined) return;

  automaticUpdateTimer = window.setTimeout(() => {
    automaticUpdateTimer = undefined;
    applyAvailableUpdate();
  }, UPDATE_ACTIVATION_GRACE_MS);
}

function applyAvailableUpdate() {
  if (
    !hadControllerAtStartup
    || !updateAvailable
    || updateApplying
    || !updateServiceWorker
  ) return;

  updateAvailable = false;
  updateApplying = true;
  clearAutomaticUpdateTimer();
  announcePwaUpdateApplying();
  scheduleReloadFallback();

  void updateServiceWorker().catch((error) => {
    updateApplying = false;
    clearReloadFallback();
    console.error('[PWA] Failed to apply update:', error);
    announceUpdateAvailable();
  });
}

async function checkForServiceWorkerUpdate(
  swUrl: string,
  registration: ServiceWorkerRegistration,
) {
  if (
    !navigator.onLine
    || registration.installing
    || registration.waiting
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
  navigator.serviceWorker.addEventListener('controllerchange', reloadForUpdate);
}

window.addEventListener(PWA_UPDATE_APPLY_EVENT, applyAvailableUpdate);

updateServiceWorker = registerSW({
  immediate: true,
  onNeedRefresh: announceUpdateAvailable,
  onNeedReload: reloadForUpdate,
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    if (registration.waiting) {
      announceUpdateAvailable();
    }

    let updateCheck: Promise<void> | null = null;
    const requestUpdate = () => {
      if (updateCheck) return;

      updateCheck = checkForServiceWorkerUpdate(swUrl, registration)
        .finally(() => {
          updateCheck = null;
        });
    };

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
