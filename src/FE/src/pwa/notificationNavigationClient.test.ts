import { describe, expect, it, vi } from 'vitest';
import {
  NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
  NOTIFICATION_NAVIGATION_REQUEST,
  NOTIFICATION_RECEIVED,
} from './notificationNavigation';
import {
  handleNotificationNavigationMessage,
  installNotificationReceivedInvalidator,
} from './notificationNavigationClient';

function createServiceWorkerContainer(): {
  serviceWorkers: ServiceWorkerContainer;
  dispatchMessage: (data: unknown) => void;
  remove: ReturnType<typeof vi.fn>;
} {
  const listeners = new Map<string, (event: MessageEvent) => void>();
  const remove = vi.fn((type: string, listener: unknown) => {
    if (listeners.get(type) === listener) listeners.delete(type);
  });
  const serviceWorkers = {
    addEventListener: vi.fn((type: string, listener: unknown) => {
      listeners.set(type, listener as (event: MessageEvent) => void);
    }),
    removeEventListener: remove,
  } as unknown as ServiceWorkerContainer;

  return {
    serviceWorkers,
    dispatchMessage: (data) => listeners.get('message')?.({ data } as MessageEvent),
    remove,
  };
}

describe('installNotificationReceivedInvalidator', () => {
  it('invalidates the job list when a push-receipt message arrives', () => {
    const invalidate = vi.fn();
    const { serviceWorkers, dispatchMessage } = createServiceWorkerContainer();

    installNotificationReceivedInvalidator(serviceWorkers, invalidate);

    dispatchMessage({ type: NOTIFICATION_RECEIVED });
    expect(invalidate).toHaveBeenCalledOnce();
  });

  it('ignores unrelated service-worker messages', () => {
    const invalidate = vi.fn();
    const { serviceWorkers, dispatchMessage } = createServiceWorkerContainer();

    installNotificationReceivedInvalidator(serviceWorkers, invalidate);

    dispatchMessage({ type: 'OTHER' });
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('removes its listener when cleaned up', () => {
    const { serviceWorkers, dispatchMessage, remove } = createServiceWorkerContainer();

    const cleanup = installNotificationReceivedInvalidator(serviceWorkers, vi.fn());
    cleanup();

    expect(remove).toHaveBeenCalledOnce();
    dispatchMessage({ type: NOTIFICATION_RECEIVED });
    expect(remove).toHaveBeenCalledOnce();
  });
});

describe('handleNotificationNavigationMessage', () => {
  it('navigates the open app through its router and acknowledges completion', async () => {
    const navigate = vi.fn().mockResolvedValue(undefined);
    const reply = vi.fn();
    const event = {
      data: {
        type: NOTIFICATION_NAVIGATION_REQUEST,
        url: 'https://app.mrsoftware.dk/app/job/job-1?source=push#details',
      },
      ports: [{ postMessage: reply }],
    } as unknown as MessageEvent;

    await expect(handleNotificationNavigationMessage(
      event,
      'https://app.mrsoftware.dk',
      navigate,
    )).resolves.toBe(true);

    expect(navigate).toHaveBeenCalledWith('/app/job/job-1?source=push#details');
    expect(reply).toHaveBeenCalledWith({
      type: NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
      success: true,
    });
  });

  it('acknowledges router failures so the service worker can fall back', async () => {
    const reply = vi.fn();
    const event = {
      data: {
        type: NOTIFICATION_NAVIGATION_REQUEST,
        url: '/app/job/job-1',
      },
      ports: [{ postMessage: reply }],
    } as unknown as MessageEvent;

    await expect(handleNotificationNavigationMessage(
      event,
      'https://app.mrsoftware.dk',
      vi.fn().mockRejectedValue(new Error('router unavailable')),
    )).rejects.toThrow('router unavailable');

    expect(reply).toHaveBeenCalledWith({
      type: NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
      success: false,
    });
  });

  it('ignores unrelated service-worker messages', async () => {
    const navigate = vi.fn();
    const event = { data: { type: 'OTHER' }, ports: [] } as unknown as MessageEvent;

    await expect(handleNotificationNavigationMessage(
      event,
      'https://app.mrsoftware.dk',
      navigate,
    )).resolves.toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });
});
