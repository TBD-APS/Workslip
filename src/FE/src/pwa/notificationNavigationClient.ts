import {
  isNotificationNavigationRequest,
  NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
  resolveNotificationTarget,
  type NotificationNavigationAcknowledgement,
} from './notificationNavigation';

export type NotificationRouteNavigator = (target: string) => Promise<unknown> | unknown;

export async function handleNotificationNavigationMessage(
  event: MessageEvent,
  origin: string,
  navigate: NotificationRouteNavigator,
): Promise<boolean> {
  if (!isNotificationNavigationRequest(event.data)) return false;

  const acknowledgement: NotificationNavigationAcknowledgement = {
    type: NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
    success: false,
  };

  try {
    const target = new URL(resolveNotificationTarget(event.data.url, origin));
    await navigate(`${target.pathname}${target.search}${target.hash}`);
    acknowledgement.success = true;
  } finally {
    event.ports[0]?.postMessage(acknowledgement);
  }

  return acknowledgement.success;
}

export function installNotificationNavigationHandler(
  serviceWorkers: ServiceWorkerContainer,
  origin: string,
  navigate: NotificationRouteNavigator,
): () => void {
  const handleMessage = (event: MessageEvent) => {
    void handleNotificationNavigationMessage(event, origin, navigate).catch(() => {
      // The acknowledgement already tells the worker to use its document-
      // navigation fallback. Avoid turning that recoverable path into an
      // unhandled rejection in the open application.
    });
  };

  serviceWorkers.addEventListener('message', handleMessage);
  return () => serviceWorkers.removeEventListener('message', handleMessage);
}
