import { describe, expect, it } from 'vitest';
import { notificationListQueryKey } from './notificationQueryKeys';

describe('notificationListQueryKey', () => {
  it('includes user and organization scope', () => {
    expect(notificationListQueryKey('user-1', 'organization-1')).not.toEqual(
      notificationListQueryKey('user-1', 'organization-2'),
    );
    expect(notificationListQueryKey('user-1', 'organization-1')).not.toEqual(
      notificationListQueryKey('user-2', 'organization-1'),
    );
  });
});
