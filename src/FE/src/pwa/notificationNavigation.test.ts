import { describe, expect, it, vi } from 'vitest';
import {
  isNotificationNavigationAcknowledgement,
  isNotificationNavigationRequest,
  isNotificationReceivedMessage,
  navigateNotificationTarget,
  NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
  NOTIFICATION_NAVIGATION_REQUEST,
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

describe('notification message guards', () => {
  it('recognises valid request, acknowledgement and receipt messages', () => {
    expect(isNotificationNavigationRequest({
      type: NOTIFICATION_NAVIGATION_REQUEST,
      url: '/app/job/job-1',
    })).toBe(true);
    expect(isNotificationNavigationAcknowledgement({
      type: NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
      success: true,
    })).toBe(true);
    expect(isNotificationReceivedMessage({ type: NOTIFICATION_RECEIVED })).toBe(true);
  });

  it.each([
    null,
    NOTIFICATION_NAVIGATION_REQUEST,
    { type: NOTIFICATION_NAVIGATION_REQUEST },
    { type: NOTIFICATION_NAVIGATION_REQUEST, url: '' },
    { type: NOTIFICATION_NAVIGATION_REQUEST, url: '   ' },
    { type: NOTIFICATION_NAVIGATION_REQUEST, url: 123 },
  ])('rejects invalid navigation requests %p', (value) => {
    expect(isNotificationNavigationRequest(value)).toBe(false);
  });

  it.each([
    null,
    { type: NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT },
    { type: NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT, success: 'yes' },
    { type: 'OTHER', success: true },
  ])('rejects invalid navigation acknowledgements %p', (value) => {
    expect(isNotificationNavigationAcknowledgement(value)).toBe(false);
  });

  it('rejects unrelated push receipt messages and non-objects', () => {
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

  it('preserves same-origin query strings and fragments', () => {
    expect(resolveNotificationTarget(
      `${APP_ORIGIN}/app/job/job-1?tab=history#latest`,
      APP_ORIGIN,
    )).toBe(`${APP_ORIGIN}/app/job/job-1?tab=history#latest`);
  });

  it.each([
    undefined,
    null,
    '',
    '   ',
    'https://example.com/phishing',
    'https://user:password@app.mrsoftware.dk/app/job/job-1',
    'http://[invalid',
  ])('falls back to the application root for unsafe target %p', (target) => {
    expect(resolveNotificationTarget(target, APP_ORIGIN)).toBe(`${APP_ORIGIN}/`);
  });
});

describe('navigateNotificationTarget', () => {
  it('awaits acknowledged app-router navigation before focusing an open client', async () => {
    let resolveNavigation: ((handled: boolean) => void) | undefined;
    const client = createClient();
    const openWindow = vi.fn();
    const navigateOpenClient = vi.fn().mockImplementation(() =>
      new Promise<boolean>((resolve) => {
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

  it('uses document navigation when router navigation returns false', async () => {
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

    expect(originalClient.navigate).toHaveBeenCalledWith(
      `${APP_ORIGIN}/app/job/job-1`,
    );
    expect(navigatedClient.focus).toHaveBeenCalledOnce();
  });

  it('uses document navigation when router navigation throws', async () => {
    const originalClient = createClient();
    const navigatedClient = createClient({
      url: `${APP_ORIGIN}/app/job/job-1`,
    });
    vi.mocked(originalClient.navigate).mockResolvedValue(navigatedClient);

    await navigateNotificationTarget(
      [originalClient],
      vi.fn(),
      '/app/job/job-1',
      APP_ORIGIN,
      vi.fn().mockRejectedValue(new Error('old client')),
    );

    expect(originalClient.navigate).toHaveBeenCalledOnce();
    expect(navigatedClient.focus).toHaveBeenCalledOnce();
  });

  it.each([
    ['null navigation result', null],
    ['wrong returned URL', createClient({ url: `${APP_ORIGIN}/app` })],
  ])('opens the target after %s', async (_label, navigatedResult) => {
    const existingClient = createClient();
    const openedClient = createClient({
      url: `${APP_ORIGIN}/app/completed/job-1`,
      focused: true,
      visibilityState: 'visible',
    });
    const openWindow = vi.fn().mockResolvedValue(openedClient);
    vi.mocked(existingClient.navigate).mockResolvedValue(
      navigatedResult as NotificationWindowClient | null,
    );

    const result = await navigateNotificationTarget(
      [existingClient],
      openWindow,
      '/app/completed/job-1',
      APP_ORIGIN,
      vi.fn().mockResolvedValue(false),
    );

    expect(openWindow).toHaveBeenCalledWith(
      `${APP_ORIGIN}/app/completed/job-1`,
    );
    expect(existingClient.focus).not.toHaveBeenCalled();
    expect(result).toBe(openedClient);
  });

  it('opens the target when document navigation throws', async () => {
    const existingClient = createClient();
    vi.mocked(existingClient.navigate).mockRejectedValue(new Error('blocked'));
    const openedClient = createClient({ url: `${APP_ORIGIN}/app/job/job-1` });
    const openWindow = vi.fn().mockResolvedValue(openedClient);

    const result = await navigateNotificationTarget(
      [existingClient],
      openWindow,
      '/app/job/job-1',
      APP_ORIGIN,
    );

    expect(result).toBe(openedClient);
    expect(openWindow).toHaveBeenCalledOnce();
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

  it('prefers a focused client over visible and hidden clients', async () => {
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
  });

  it('prefers a visible client when no eligible client is focused', async () => {
    const hiddenClient = createClient();
    const visibleClient = createClient({ visibilityState: 'visible' });
    const navigateOpenClient = vi.fn().mockResolvedValue(true);

    await navigateNotificationTarget(
      [hiddenClient, visibleClient],
      vi.fn(),
      '/app/job/job-1',
      APP_ORIGIN,
      navigateOpenClient,
    );

    expect(navigateOpenClient).toHaveBeenCalledWith(
      visibleClient,
      `${APP_ORIGIN}/app/job/job-1`,
    );
  });

  it.each([
    '/',
    '/login',
    '/app',
    '/app/jobs',
    '/invite/token',
    '/superadmin',
    '/superadmin/organizations',
  ])('recognises %s as an eligible Workslip client route', async (path) => {
    const client = createClient({ url: `${APP_ORIGIN}${path}` });
    const navigateOpenClient = vi.fn().mockResolvedValue(true);

    await navigateNotificationTarget(
      [client],
      vi.fn(),
      '/app/job/job-1',
      APP_ORIGIN,
      navigateOpenClient,
    );

    expect(navigateOpenClient).toHaveBeenCalledWith(
      client,
      `${APP_ORIGIN}/app/job/job-1`,
    );
  });

  it.each([
    `${APP_ORIGIN}/robots.txt`,
    'https://example.com/app',
    'https://user:password@app.mrsoftware.dk/app',
    'not-a-url',
  ])('ignores ineligible client URL %s', async (url) => {
    const unrelatedClient = createClient({
      url,
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

  it('opens a safe fallback when no app client exists and target is external', async () => {
    const openedClient = createClient({ url: `${APP_ORIGIN}/` });
    const openWindow = vi.fn().mockResolvedValue(openedClient);

    const result = await navigateNotificationTarget(
      [],
      openWindow,
      'https://example.com/phishing',
      APP_ORIGIN,
    );

    expect(openWindow).toHaveBeenCalledWith(`${APP_ORIGIN}/`);
    expect(result).toBe(openedClient);
  });

  it.each([
    ['openWindow returns null', vi.fn().mockResolvedValue(null)],
    ['openWindow throws', vi.fn().mockRejectedValue(new Error('blocked'))],
  ])('focuses the existing app as last resort when %s', async (_label, openWindow) => {
    const existingClient = createClient();
    vi.mocked(existingClient.navigate).mockResolvedValue(null);

    const result = await navigateNotificationTarget(
      [existingClient],
      openWindow,
      '/app/job/job-1',
      APP_ORIGIN,
    );

    expect(existingClient.focus).toHaveBeenCalledOnce();
    expect(result).toBe(existingClient);
  });
});
