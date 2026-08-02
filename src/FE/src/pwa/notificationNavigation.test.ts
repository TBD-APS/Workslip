import { describe, expect, it, vi } from 'vitest';
import {
  isNotificationReceivedMessage,
  navigateNotificationTarget,
  NOTIFICATION_RECEIVED,
  resolveNotificationTarget,
  type NotificationWindowClient,
} from './notificationNavigation';

const APP_ORIGIN = 'https://app.mrsoftware.dk';

function createClient(
  overrides: Partial<NotificationWindowClient> = {},
): NotificationWindowClient {
  const client = {
    url: `${APP_ORIGIN}/app`,
    focused: false,
    visibilityState: 'hidden',
    navigate: vi.fn(),
    focus: vi.fn(),
    ...overrides,
  } as NotificationWindowClient;

  vi.mocked(client.navigate).mockResolvedValue(client);
  vi.mocked(client.focus).mockResolvedValue(client);
  return client;
}

describe('isNotificationReceivedMessage', () => {
  it('recognises a push-receipt message', () => {
    expect(isNotificationReceivedMessage({ type: NOTIFICATION_RECEIVED })).toBe(true);
  });

  it('rejects unrelated messages and non-objects', () => {
    expect(isNotificationReceivedMessage({ type: 'OTHER' })).toBe(false);
    expect(isNotificationReceivedMessage(null)).toBe(false);
    expect(isNotificationReceivedMessage(NOTIFICATION_RECEIVED)).toBe(false);
  });
});

describe('resolveNotificationTarget', () => {
  it('resolves relative notification routes against the application origin', () => {
    expect(resolveNotificationTarget('/app/job/job-1', APP_ORIGIN))
      .toBe(`${APP_ORIGIN}/app/job/job-1`);
  });

  it('rejects external and invalid notification targets', () => {
    expect(resolveNotificationTarget('https://example.com/phishing', APP_ORIGIN))
      .toBe(`${APP_ORIGIN}/`);
    expect(resolveNotificationTarget('http://[invalid', APP_ORIGIN))
      .toBe(`${APP_ORIGIN}/`);
  });
});

describe('navigateNotificationTarget', () => {
  it('awaits acknowledged app-router navigation before focusing an open client', async () => {
    let resolveNavigation: ((handled: boolean) => void) | undefined;
    const client = createClient();
    const openWindow = vi.fn();
    const navigateOpenClient = vi.fn().mockImplementation(() => new Promise<boolean>((resolve) => {
      resolveNavigation = resolve;
    }));

    const navigation = navigateNotificationTarget(
      [client],
      openWindow,
      '/app/job/job-1',
      APP_ORIGIN,
      navigateOpenClient,
    );

    expect(navigateOpenClient).toHaveBeenCalledWith(
      client,
      `${APP_ORIGIN}/app/job/job-1`,
    );
    expect(client.navigate).not.toHaveBeenCalled();
    expect(client.focus).not.toHaveBeenCalled();
    expect(openWindow).not.toHaveBeenCalled();

    resolveNavigation?.(true);
    await navigation;

    expect(client.focus).toHaveBeenCalledOnce();
  });

  it('uses document navigation when the open app cannot handle the route', async () => {
    const originalClient = createClient();
    const navigatedClient = createClient({
      url: `${APP_ORIGIN}/app/job/job-1`,
      focused: true,
      visibilityState: 'visible',
    });
    vi.mocked(originalClient.navigate).mockResolvedValue(navigatedClient);

    await navigateNotificationTarget(
      [originalClient],
      vi.fn(),
      '/app/job/job-1',
      APP_ORIGIN,
      vi.fn().mockResolvedValue(false),
    );

    expect(originalClient.navigate).toHaveBeenCalledWith(`${APP_ORIGIN}/app/job/job-1`);
    expect(navigatedClient.focus).toHaveBeenCalledOnce();
    expect(originalClient.focus).not.toHaveBeenCalled();
  });

  it('opens the target when document navigation cannot be confirmed', async () => {
    const existingClient = createClient();
    const openedClient = createClient({
      url: `${APP_ORIGIN}/app/completed/job-1`,
      focused: true,
      visibilityState: 'visible',
    });
    const openWindow = vi.fn().mockResolvedValue(openedClient);
    vi.mocked(existingClient.navigate).mockResolvedValue(null);

    const result = await navigateNotificationTarget(
      [existingClient],
      openWindow,
      '/app/completed/job-1',
      APP_ORIGIN,
      vi.fn().mockResolvedValue(false),
    );

    expect(existingClient.navigate).toHaveBeenCalledWith(`${APP_ORIGIN}/app/completed/job-1`);
    expect(openWindow).toHaveBeenCalledWith(`${APP_ORIGIN}/app/completed/job-1`);
    expect(existingClient.focus).not.toHaveBeenCalled();
    expect(result).toBe(openedClient);
  });

  it('focuses the client returned from confirmed document navigation', async () => {
    const originalClient = createClient();
    const navigatedClient = createClient({
      url: `${APP_ORIGIN}/app/completed/job-1`,
      focused: true,
      visibilityState: 'visible',
    });
    vi.mocked(originalClient.navigate).mockResolvedValue(navigatedClient);

    await navigateNotificationTarget(
      [originalClient],
      vi.fn(),
      '/app/completed/job-1',
      APP_ORIGIN,
    );

    expect(navigatedClient.focus).toHaveBeenCalledOnce();
    expect(originalClient.focus).not.toHaveBeenCalled();
  });

  it('prefers the focused client and then a visible client', async () => {
    const hiddenClient = createClient();
    const visibleClient = createClient({ visibilityState: 'visible' });
    const focusedClient = createClient({ focused: true });
    const navigateOpenClient = vi.fn().mockResolvedValue(true);

    await navigateNotificationTarget(
      [hiddenClient, visibleClient, focusedClient],
      vi.fn(),
      '/app/job/job-1',
      APP_ORIGIN,
      navigateOpenClient,
    );

    expect(navigateOpenClient).toHaveBeenCalledWith(
      focusedClient,
      `${APP_ORIGIN}/app/job/job-1`,
    );
    expect(visibleClient.focus).not.toHaveBeenCalled();
    expect(hiddenClient.focus).not.toHaveBeenCalled();
  });

  it('ignores focused same-origin windows that are not Workslip pages', async () => {
    const unrelatedClient = createClient({
      url: `${APP_ORIGIN}/robots.txt`,
      focused: true,
      visibilityState: 'visible',
    });
    const appClient = createClient();
    const navigateOpenClient = vi.fn().mockResolvedValue(true);

    await navigateNotificationTarget(
      [unrelatedClient, appClient],
      vi.fn(),
      '/app/job/job-1',
      APP_ORIGIN,
      navigateOpenClient,
    );

    expect(navigateOpenClient).toHaveBeenCalledWith(
      appClient,
      `${APP_ORIGIN}/app/job/job-1`,
    );
    expect(unrelatedClient.focus).not.toHaveBeenCalled();
  });

  it('opens the target when no application client exists', async () => {
    const openedClient = createClient({ focused: true, visibilityState: 'visible' });
    const openWindow = vi.fn().mockResolvedValue(openedClient);

    const result = await navigateNotificationTarget(
      [],
      openWindow,
      '/app/job/job-1',
      APP_ORIGIN,
    );

    expect(openWindow).toHaveBeenCalledWith(`${APP_ORIGIN}/app/job/job-1`);
    expect(result).toBe(openedClient);
  });
});
