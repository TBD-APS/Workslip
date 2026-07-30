export const PWA_UPDATE_READY_EVENT = 'workslip:pwa-update-ready';
export const PWA_UPDATE_APPLY_EVENT = 'workslip:pwa-update-apply';
export const PWA_UPDATE_APPLYING_EVENT = 'workslip:pwa-update-applying';
export const PWA_UPDATE_COORDINATOR_READY_EVENT = 'workslip:pwa-update-coordinator-ready';

export function announcePwaUpdateReady() {
  window.dispatchEvent(new Event(PWA_UPDATE_READY_EVENT));
}

export function announcePwaUpdateApplying() {
  window.dispatchEvent(new Event(PWA_UPDATE_APPLYING_EVENT));
}

export function announcePwaUpdateCoordinatorReady() {
  window.dispatchEvent(new Event(PWA_UPDATE_COORDINATOR_READY_EVENT));
}

export function requestPwaUpdate() {
  window.dispatchEvent(new Event(PWA_UPDATE_APPLY_EVENT));
}
