/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';
import {
  announcePwaUpdateApplying,
  announcePwaUpdateCoordinatorReady,
  announcePwaUpdateReady,
  PWA_UPDATE_APPLY_EVENT,
} from './lib/pwaUpdateEvents';

const UPDATE_INTERVAL_MS = 60 * 1000;
const UPDATE_ACTIVATION_GRACE_MS = 10_000;
const UPDATE_RELOAD_FALLBACK_MS = 5_000;
const WAITING_WORKER_RESOLVE_TIMEOUT_MS = 2_000;
const WAITING_WORKER_POLL_INTERVAL_MS = 50;
const FALLBACK_RELOAD_KEY = `workslip.pwaUpdateFallback:${__BUILD_TIME__}`;
const SKIP_WAITING_MESSAGE = { type: 'SKIP_WAITING' } as const;

const serviceWorkerSupported = 'serviceWorker' in navigator;
const hadControllerAtStartup = serviceWorkerSupported
  && Boolean(navigator.serviceWorker.controller);

let reloadRequested = false;
let reloadFallback: number | undefined;
let automaticUpdateTimer: number | undefined;
let updateAvailable = false;
let updateApplying = false;
let updateActivationResolving = false;
let serviceWorkerRegistration: ServiceWorkerRegistration | null = null;
let requestRegisteredUpdate: (() => void) | null = null;

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
    // The document-level guards still prevent duplicate work in this page.
  }
}

function wasFallbackReloadUsedForCurrentBuild() {
  return readSessionValue(FALLBACK_RELOAD_KEY) === '1';
}

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, milliseconds);
  });
}

async function resolveServiceWorkerRegistration() {
  if (serviceWorkerRegistration) return serviceWorkerRegistration;

  const resolvedRegistration = await navigator.serviceWorker.getRegistration();
  serviceWorkerRegistration = resolvedRegistration ?? null;
  return serviceWorkerRegistration;
}

async function waitForWaitingWorker() {
  if (!serviceWorkerSupported) return null;

  const registration = await resolveServiceWorkerRegistration();
  if (!registration) return null;

  const deadline = Date.now() + WAITING_WORKER_RESOLVE_TIMEOUT_MS;
  while (true) {
    if (registration.waiting) return registration.waiting;
    if (Date.now() >= deadline) return null;
    await delay(WAITING_WORKER_POLL_INTERVAL_MS);
  }
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
  if (!hadControllerAtStartup || reloadRequested) return;

  reloadRequested = true;
  clearAutomaticUpdateTimer();
  clearReloadFallback();
  window.location.reload();
}

function recoverFromActivationTimeout() {
  reloadFallback = undefined;
  if (reloadRequested) return;

  updateApplying = false;
  updateAvailable = Boolean(serviceWorkerRegistration?.waiting);

  if (updateAvailable) {
    // Keep the action usable, but do not schedule another automatic attempt for
    // the same old build after its one permitted emergency reload was consumed.
    announcePwaUpdateReady();
  } else {
    requestRegisteredUpdate?.();
  }
}

function reloadFromFallback() {
  reloadFallback = undefined;
  if (!hadControllerAtStartup || reloadRequested) return;

  writeSessionValue(FALLBACK_RELOAD_KEY, '1');
  reloadForUpdate();
}

function scheduleReloadFallback() {
  if (!hadControllerAtStartup || reloadRequested || reloadFallback !== undefined) return;

  const fallbackAction = wasFallbackReloadUsedForCurrentBuild()
    ? recoverFromActivationTimeout
    : reloadFromFallback;
  reloadFallback = window.setTimeout(fallbackAction, UPDATE_RELOAD_FALLBACK_MS);
}

function announceUpdateAvailable() {
  if (!hadControllerAtStartup || updateApplying) return;

  updateAvailable = true;
  announcePwaUpdateReady();

  if (
    automaticUpdateTimer !== undefined
    || wasFallbackReloadUsedForCurrentBuild()
  ) return;

  automaticUpdateTimer = window.setTimeout(() => {
    automaticUpdateTimer = undefined;
    void applyAvailableUpdate();
  }, UPDATE_ACTIVATION_GRACE_MS);
}

async function resolveRegistrationAndAnnounceUpdate() {
  if (!serviceWorkerSupported || updateApplying) return;

  const waitingWorker = await waitForWaitingWorker();
  if (waitingWorker) {
    announceUpdateAvailable();
  } else {
    requestRegisteredUpdate?.();
  }
}

function observeInstallingWorker(installingWorker: ServiceWorker | null) {
  if (!installingWorker) return;

  const handleStateChange = () => {
    if (installingWorker.state === 'installed' && hadControllerAtStartup) {
      void resolveRegistrationAndAnnounceUpdate();
    }
  };

  installingWorker.addEventListener('statechange', handleStateChange);
  handleStateChange();
}

function observeRegistrationUpdates(registration: ServiceWorkerRegistration) {
  registration.addEventListener('updatefound', () => {
    observeInstallingWorker(registration.installing);
  });

  observeInstallingWorker(registration.installing);
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

async function applyAvailableUpdate() {
  if (
    !hadControllerAtStartup
    || !updateAvailable
    || updateApplying
    || updateActivationResolving
  ) return;

  updateActivationResolving = true;
  try {
    const waitingWorker = await waitForWaitingWorker();
    if (!waitingWorker) {
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
  } finally {
    updateActivationResolving = false;
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
  // Native controller changes and the plugin's controlling callback both feed
  // this single guarded reload coordinator. The worker never navigates clients.
  navigator.serviceWorker.addEventListener('controllerchange', reloadForUpdate);
}

window.addEventListener(PWA_UPDATE_APPLY_EVENT, () => {
  void applyAvailableUpdate();
});

registerSW({
  immediate: true,
  onNeedRefresh() {
    // Redundant plugin signal. Native registration events below are authoritative
    // for updates started through registration.update().
    void resolveRegistrationAndAnnounceUpdate();
  },
  // vite-plugin-pwa otherwise reloads directly from its Workbox controlling
  // listener. Route that signal through the same one-shot coordinator instead.
  onNeedReload: reloadForUpdate,
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    serviceWorkerRegistration = registration;
    observeRegistrationUpdates(registration);

    let updateCheck: Promise<void> | null = null;
    const requestUpdate = () => {
      if (updateCheck) return updateCheck;

      updateCheck = checkForServiceWorkerUpdate(swUrl, registration)
        .finally(() => {
          updateCheck = null;
        });
      return updateCheck;
    };

    requestRegisteredUpdate = () => {
      void requestUpdate();
    };

    if (registration.waiting) {
      announceUpdateAvailable();
    }

    // Discover a deployment immediately when the app starts, returns to the
    // foreground or regains connectivity, and at most one minute afterwards.
    // Readiness is announced only after this first check settles, preventing a
    // newly opened client from overlapping its bootstrap check with deployment.
    void requestUpdate().finally(announcePwaUpdateCoordinatorReady);
    window.setInterval(() => {
      void requestUpdate();
    }, UPDATE_INTERVAL_MS);
    window.addEventListener('online', () => {
      void requestUpdate();
    });

    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') {
        void requestUpdate();
      }
    });
  },
  onRegisterError(error) {
    console.error('[PWA] Registration failed:', error);
  },
});
