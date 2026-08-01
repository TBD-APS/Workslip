import { describe, expect, it, vi } from 'vitest';
import {
  NOTIFICATION_NAVIGATION_ACKNOWLEDGEMENT,
  NOTIFICATION_NAVIGATION_REQUEST,
} from './notificationNavigation';
import { handleNotificationNavigationMessage } from './notificationNavigationClient';

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
