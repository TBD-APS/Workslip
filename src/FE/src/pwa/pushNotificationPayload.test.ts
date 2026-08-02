import { describe, expect, it } from 'vitest';
import { normalizePushNotificationPayload } from './pushNotificationPayload';

describe('normalizePushNotificationPayload', () => {
  it('preserves a valid notification payload', () => {
    expect(normalizePushNotificationPayload({
      title: 'Assigned job',
      options: {
        body: 'Open the job',
        icon: '/icons/icon-192.png',
        badge: '/icons/badge.png',
        tag: ' job-123 ',
        data: { url: '/app/job/123', notificationId: 'notification-1' },
      },
    })).toEqual({
      title: 'Assigned job',
      options: {
        body: 'Open the job',
        icon: '/icons/icon-192.png',
        badge: '/icons/badge.png',
        tag: 'job-123',
        data: { url: '/app/job/123', notificationId: 'notification-1' },
      },
    });
  });

  it.each([
    undefined,
    null,
    'not-an-object',
    42,
    [],
  ])('uses a complete fallback for invalid root value %p', (value) => {
    expect(normalizePushNotificationPayload(value)).toEqual({
      title: 'Workslip',
      options: {
        body: 'You have a new notification',
        icon: '/logo.png',
        badge: '/logo.png',
        tag: '',
        data: {},
      },
    });
  });

  it('replaces empty and incorrectly typed display fields', () => {
    expect(normalizePushNotificationPayload({
      title: '   ',
      options: {
        body: '',
        icon: 123,
        badge: null,
        tag: false,
      },
    })).toEqual({
      title: 'Workslip',
      options: {
        body: 'You have a new notification',
        icon: '/logo.png',
        badge: '/logo.png',
        tag: '',
        data: {},
      },
    });
  });

  it.each([
    null,
    'url=/app',
    ['/app/job/123'],
  ])('rejects non-object notification data %p', (data) => {
    expect(normalizePushNotificationPayload({ options: { data } }).options.data)
      .toEqual({});
  });

  it('does not mutate the source data object', () => {
    const data = { url: '/app/job/123' };
    const normalized = normalizePushNotificationPayload({ options: { data } });

    expect(normalized.options.data).toBe(data);
    expect(data).toEqual({ url: '/app/job/123' });
  });
});
