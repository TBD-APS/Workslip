import { describe, expect, it } from 'vitest';
import { createQueryClient } from './createQueryClient';
import {
  notificationListQueryKey,
  NOTIFICATION_QUERY_PREFIX,
} from './notificationQueryKeys';
import {
  NOTIFICATION_LIST_GC_TIME_MS,
  NOTIFICATION_LIST_REFETCH_INTERVAL_MS,
  NOTIFICATION_LIST_STALE_TIME_MS,
} from './queryTimings';

describe('createQueryClient', () => {
  it('keeps the notification family current while the authenticated layout is open', () => {
    const client = createQueryClient();

    expect(client.getQueryDefaults(NOTIFICATION_QUERY_PREFIX)).toMatchObject({
      staleTime: NOTIFICATION_LIST_STALE_TIME_MS,
      gcTime: NOTIFICATION_LIST_GC_TIME_MS,
      refetchInterval: NOTIFICATION_LIST_REFETCH_INTERVAL_MS,
      refetchIntervalInBackground: true,
      refetchOnWindowFocus: true,
    });
  });

  it('separates notification caches across users and organizations', () => {
    expect(notificationListQueryKey('user-1', 'organization-1')).not.toEqual(
      notificationListQueryKey('user-1', 'organization-2'),
    );
    expect(notificationListQueryKey('user-1', 'organization-1')).not.toEqual(
      notificationListQueryKey('user-2', 'organization-1'),
    );
  });
});
