export interface NormalizedPushNotification {
  title: string;
  options: {
    body: string;
    icon: string;
    badge: string;
    tag: string;
    data: Record<string, unknown>;
  };
}

const DEFAULT_NOTIFICATION: NormalizedPushNotification = {
  title: 'Workslip',
  options: {
    body: 'You have a new notification',
    icon: '/logo.png',
    badge: '/logo.png',
    tag: '',
    data: {},
  },
};

function nonEmptyString(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim().length > 0
    ? value
    : fallback;
}

function plainRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object'
    && value !== null
    && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

export function normalizePushNotificationPayload(
  value: unknown,
): NormalizedPushNotification {
  const payload = plainRecord(value);
  const options = plainRecord(payload.options);

  return {
    title: nonEmptyString(payload.title, DEFAULT_NOTIFICATION.title),
    options: {
      body: nonEmptyString(options.body, DEFAULT_NOTIFICATION.options.body),
      icon: nonEmptyString(options.icon, DEFAULT_NOTIFICATION.options.icon),
      badge: nonEmptyString(options.badge, DEFAULT_NOTIFICATION.options.badge),
      tag: typeof options.tag === 'string' ? options.tag.trim() : '',
      data: plainRecord(options.data),
    },
  };
}
