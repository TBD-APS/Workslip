const NOTIFICATION_API_PATH = '/api/notifications';

export const NOTIFICATION_QUERY_PREFIX = [NOTIFICATION_API_PATH] as const;

export function notificationListQueryKey(userId: string, limit = 50) {
  return [NOTIFICATION_API_PATH, userId, { limit }] as const;
}
