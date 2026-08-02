export interface NotificationWindowClient {
  url: string;
  focused: boolean;
  visibilityState: string;
  navigate(url: string): Promise<NotificationWindowClient | null>;
  focus(): Promise<NotificationWindowClient>;
}

export const NOTIFICATION_NAVIGATION_REQUEST = 'WORKSLIP_NOTIFICATION_NAVIGATION';
export const NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT = 'WORKSLIP_NOTIFICATION_NAVIGATION_ACK';
export const NOTIFICATION_RECEIVED = 'WORKSLIP_NOTIFICATION_RECEIVED';

export interface NotificationNavigationRequest {
  type: typeof NOTIFICATION_NAVIGATION_REQUEST;
  url: string;
}

export interface NotificationNavigationAcknowledgement {
  type: typeof NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT;
  success: boolean;
}

export interface NotificationReceivedMessage {
  type: typeof NOTIFICATION_RECEIVED;
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
    && typeof candidate.url === 'string'
    && candidate.url.trim().length > 0;
}

export function isNotificationNavigationAcknowledgement(
  value: unknown,
): value is NotificationNavigationAcknowledgement {
  if (typeof value !== 'object' || value === null) return false;

  const candidate = value as Partial<NotificationNavigationAcknowledgement>;
  return candidate.type === NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT
    && typeof candidate.success === 'boolean';
}

export function isNotificationReceivedMessage(
  value: unknown,
): value is NotificationReceivedMessage {
  return typeof value === 'object'
    && value !== null
    && (value as Partial<NotificationReceivedMessage>).type === NOTIFICATION_RECEIVED;
}

export function resolveNotificationTarget(rawTarget: unknown, origin: string): string {
  const fallback = new URL('/', origin);

  if (typeof rawTarget !== 'string' || rawTarget.trim() === '') {
    return fallback.href;
  }

  try {
    const target = new URL(rawTarget, fallback);
    return target.origin === fallback.origin
      && target.username === ''
      && target.password === ''
      ? target.href
      : fallback.href;
  } catch {
    return fallback.href;
  }
}

function isWorkslipClient(
  client: NotificationWindowClient,
  origin: string,
): boolean {
  try {
    const url = new URL(client.url);
    if (url.origin !== origin || url.username !== '' || url.password !== '') {
      return false;
    }

    return url.pathname === '/'
      || url.pathname === '/login'
      || url.pathname === '/app'
      || url.pathname.startsWith('/app/')
      || url.pathname.startsWith('/invite/')
      || url.pathname === '/superadmin'
      || url.pathname.startsWith('/superadmin/');
  } catch {
    return false;
  }
}

function selectNotificationClient(
  clients: readonly NotificationWindowClient[],
  origin: string,
): NotificationWindowClient | undefined {
  const eligibleClients = clients.filter((client) => isWorkslipClient(client, origin));

  return eligibleClients.find((client) => client.focused)
    ?? eligibleClients.find((client) => client.visibilityState === 'visible')
    ?? eligibleClients[0];
}

function clientIsAtTarget(
  client: NotificationWindowClient,
  target: string,
): boolean {
  try {
    return new URL(client.url).href === target;
  } catch {
    return false;
  }
}

export async function navigateNotificationTarget(
  clients: readonly NotificationWindowClient[],
  openWindow: OpenNotificationWindow,
  rawTarget: unknown,
  origin: string,
  navigateOpenClient?: NavigateOpenNotificationClient,
): Promise<NotificationWindowClient | null> {
  const target = resolveNotificationTarget(rawTarget, origin);
  const existingClient = selectNotificationClient(clients, origin);

  if (!existingClient) {
    return openWindow(target);
  }

  if (navigateOpenClient) {
    try {
      if (await navigateOpenClient(existingClient, target)) {
        return existingClient.focus();
      }
    } catch {
      // Older clients do not have the router listener. Continue through the
      // document-navigation and open-window compatibility fallbacks.
    }
  }

  try {
    const navigatedClient = await existingClient.navigate(target);
    if (navigatedClient && clientIsAtTarget(navigatedClient, target)) {
      return navigatedClient.focus();
    }
  } catch {
    // Some installed-PWA clients reject or ignore document navigation. The
    // browser-level openWindow fallback below is more reliable in that state.
  }

  try {
    const openedClient = await openWindow(target);
    if (openedClient) return openedClient;
  } catch {
    // Last resort: at least bring the existing application window forward.
  }

  return existingClient.focus();
}
