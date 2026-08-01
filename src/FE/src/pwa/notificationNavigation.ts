export interface NotificationWindowClient {
  focused: boolean;
  visibilityState: string;
  navigate(url: string): Promise<NotificationWindowClient | null>;
  focus(): Promise<NotificationWindowClient>;
}

export type OpenNotificationWindow = (
  url: string,
) => Promise<NotificationWindowClient | null>;

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
): Promise<NotificationWindowClient | null> {
  const target = resolveNotificationTarget(rawTarget, origin);
  const existingClient = selectNotificationClient(clients);

  if (!existingClient) {
    return openWindow(target);
  }

  const navigatedClient = await existingClient.navigate(target);
  return (navigatedClient ?? existingClient).focus();
}
