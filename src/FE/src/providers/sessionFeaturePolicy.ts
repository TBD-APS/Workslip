import { hasPermission } from './permissions';

export function canUseSessionNotifications(role: string | null | undefined): boolean {
  return hasPermission(role, 'notification:use');
}
