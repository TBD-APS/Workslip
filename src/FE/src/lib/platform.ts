export type Platform = 'ios' | 'android' | 'desktop';

export const DESKTOP_ONLY_SUPERADMIN_MESSAGE =
  'Superadmin er kun tilgængelig på computer.';

export interface PlatformNavigator {
  userAgent: string;
  maxTouchPoints?: number;
}

export function detectPlatform(
  platformNavigator: PlatformNavigator = navigator,
): Platform {
  const ua = platformNavigator.userAgent;
  const isIPadOSDesktopUserAgent = /Macintosh|Mac OS X/i.test(ua)
    && (platformNavigator.maxTouchPoints ?? 0) > 1;

  if (/iPad|iPhone|iPod/i.test(ua) || isIPadOSDesktopUserAgent) return 'ios';
  if (/android/i.test(ua)) return 'android';
  return 'desktop';
}

export function isMobile(platformNavigator: PlatformNavigator = navigator): boolean {
  const p = detectPlatform(platformNavigator);
  return p === 'ios' || p === 'android';
}

export function isDesktopPlatform(
  platformNavigator: PlatformNavigator = navigator,
): boolean {
  return detectPlatform(platformNavigator) === 'desktop';
}

export function assertDesktopSuperadminAvailable(): void {
  if (!isDesktopPlatform()) {
    throw new Error(DESKTOP_ONLY_SUPERADMIN_MESSAGE);
  }
}
