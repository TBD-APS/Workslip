export interface NotificationWindowClient {
  focused: boolean;
  visibilityState: string;
  navigate(url: string): Promise<NotificationWindowClient | null>;
  focus(): Promise<NotificationWindowClient>;
}

export const NOTIFICATION_NAVIGATION_REQUEST = 'WORKSLIP_NOTIFICATION_NAVIGATION';
export const NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT = 'WORKSLIP_NOTIFICATION_NAVIGATION_ACK';

export interface NotificationNavigationRequest {
  type: typeof NOTIFICATION_NAVIGATION_REQUEST;
  url: string;
}

export interface NotificationNavigationAcknowledgement {
  type: typeof NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT;
  success: boolean;
}

export type OpenNotificationWindow = (
  url: string,
) => Promise<NotificationWindowClient | null>;

export type NavigateOpenNotificationClient = (
  client: NotificationWindowClient,
  url: string,
) => Promise<boolean>;

export function isNotificationNavigationRequest(
  value: unknown,
): value is NotificationNavigationRequest {
  if (typeof value !== 'object' || value === null) return false;

  const candidate = value as Partial<NotificationNavigationRequest>;
  return candidate.type === NOTIFICATION_NAVIGATION_REQUEST
    && typeof candidate.url === 'string';
}

export function isNotificationNavigationAcknowledgement(
  value: unknown,
): value is NotificationNavigationAcknowledgement {
  if (typeof value !== 'object' || value === null) return false;

  const candidate = value as Partial<NotificationNavigationAcknowledgement>;
  return candidate.type === NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT
    && typeof candidate.success === 'boolean';
}

export function resolveNotificationTarget(rawTarget: unknown, origin: string): string {
  const fallback = new URL('/', origin);

  if (typeof rawTarget !== 'string' || rawTarget.trim() === '') {
    return fallback.href;
  }

  try {
    const target = new URL(rawTarget, fallback);
    return target.origin === fallback.origin ? target.href : fallback.href;
  } catch {
    return fallback.href;
  }
}

function selectNotificationClient(
  clients: readonly NotificationWindowClient[],
): NotificationWindowClient | undefined {
  return clients.find((client) => client.focused)
    ?? clients.find((client) => client.visibilityState === 'visible')
    ?? clients[0];
}

export async function navigateNotificationTarget(
  clients: readonly NotificationWindowClient[],
  openWindow: OpenNotificationWindow,
  rawTarget: unknown,
  origin: string,
  navigateOpenClient?: NavigateOpenNotificationClient,
): Promise<NotificationWindowClient | null> {
  const target = resolveNotificationTarget(rawTarget, origin);
  const existingClient = selectNotificationClient(clients);

  if (!existingClient) {
    return openWindow(target);
  }

  if (navigateOpenClient) {
    try {
      if (await navigateOpenClient(existingClient, target)) {
        return existingClient.focus();
      }
    } catch {
      // Older clients do not have the router listener. Fall back to a document
      // navigation so notification clicks still work across deployments.
    }
  }

  const navigatedClient = await existingClient.navigate(target);
  return (navigatedClient ?? existingClient).focus();
}
