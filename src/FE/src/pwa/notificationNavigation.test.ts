import { describe, expect, it, vi } from 'vitest';
import {
  navigateNotificationTarget,
  resolveNotificationTarget,
  type NotificationWindowClient,
} from './notificationNavigation';

function createClient(
  overrides: Partial<NotificationWindowClient> = {},
): NotificationWindowClient {
  const client = {
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

describe('resolveNotificationTarget', () => {
  it('resolves relative notification routes against the application origin', () => {
    expect(resolveNotificationTarget('/app/job/job-1', 'https://app.mrsoftware.dk'))
      .toBe('https://app.mrsoftware.dk/app/job/job-1');
  });

  it('rejects external and invalid notification targets', () => {
    expect(resolveNotificationTarget('https://example.com/phishing', 'https://app.mrsoftware.dk'))
      .toBe('https://app.mrsoftware.dk/');
    expect(resolveNotificationTarget('http://[invalid', 'https://app.mrsoftware.dk'))
      .toBe('https://app.mrsoftware.dk/');
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
      'https://app.mrsoftware.dk',
      navigateOpenClient,
    );

    expect(navigateOpenClient).toHaveBeenCalledWith(
      client,
      'https://app.mrsoftware.dk/app/job/job-1',
    );
    expect(client.navigate).not.toHaveBeenCalled();
    expect(client.focus).not.toHaveBeenCalled();
    expect(openWindow).not.toHaveBeenCalled();

    resolveNavigation?.(true);
    await navigation;

    expect(client.focus).toHaveBeenCalledOnce();
  });

  it('falls back to document navigation when the open app cannot handle the route', async () => {
    const client = createClient();

    await navigateNotificationTarget(
      [client],
      vi.fn(),
      '/app/job/job-1',
      'https://app.mrsoftware.dk',
      vi.fn().mockResolvedValue(false),
    );

    expect(client.navigate).toHaveBeenCalledWith('https://app.mrsoftware.dk/app/job/job-1');
    expect(client.focus).toHaveBeenCalledOnce();
  });

  it('focuses the client returned from navigation', async () => {
    const originalClient = createClient();
    const navigatedClient = createClient({ focused: true, visibilityState: 'visible' });
    vi.mocked(originalClient.navigate).mockResolvedValue(navigatedClient);

    await navigateNotificationTarget(
      [originalClient],
      vi.fn(),
      '/app/completed/job-1',
      'https://app.mrsoftware.dk',
    );

    expect(navigatedClient.focus).toHaveBeenCalledOnce();
    expect(originalClient.focus).not.toHaveBeenCalled();
  });

  it('prefers the focused client and then a visible client', async () => {
    const hiddenClient = createClient();
    const visibleClient = createClient({ visibilityState: 'visible' });
    const focusedClient = createClient({ focused: true });

    await navigateNotificationTarget(
      [hiddenClient, visibleClient, focusedClient],
      vi.fn(),
      '/app/job/job-1',
      'https://app.mrsoftware.dk',
    );

    expect(focusedClient.navigate).toHaveBeenCalledOnce();
    expect(visibleClient.navigate).not.toHaveBeenCalled();
    expect(hiddenClient.navigate).not.toHaveBeenCalled();
  });

  it('opens the target in a new window only when no application client exists', async () => {
    const openedClient = createClient({ focused: true, visibilityState: 'visible' });
    const openWindow = vi.fn().mockResolvedValue(openedClient);

    const result = await navigateNotificationTarget(
      [],
      openWindow,
      '/app/job/job-1',
      'https://app.mrsoftware.dk',
    );

    expect(openWindow).toHaveBeenCalledWith('https://app.mrsoftware.dk/app/job/job-1');
    expect(result).toBe(openedClient);
  });
});
