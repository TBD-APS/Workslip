import { describe, expect, it } from 'vitest';
import { createQueryClient } from './createQueryClient';
import { NOTIFICATION_QUERY_PREFIX } from './notificationQueryKeys';
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
});
