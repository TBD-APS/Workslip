import { describe, expect, it, vi } from 'vitest';
import {
  DESKTOP_ONLY_SUPERADMIN_MESSAGE,
  assertDesktopSuperadminAvailable,
  detectPlatform,
  isDesktopPlatform,
  isMobile,
} from './platform';

describe('platform detection', () => {
  it.each([
    ['iPhone', {
      userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',
      maxTouchPoints: 5,
    }, 'ios'],
    ['Android', {
      userAgent: 'Mozilla/5.0 (Linux; Android 15; Pixel 9)',
      maxTouchPoints: 5,
    }, 'android'],
    ['iPadOS desktop user agent without a platform value', {
      userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15) AppleWebKit/605.1.15',
      maxTouchPoints: 5,
    }, 'ios'],
    ['Mac desktop', {
      userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)',
      maxTouchPoints: 0,
    }, 'desktop'],
    ['Windows desktop', {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
      maxTouchPoints: 0,
    }, 'desktop'],
  ] as const)('detects %s as %s', (_name, platformNavigator, expected) => {
    expect(detectPlatform(platformNavigator)).toBe(expected);
  });

  it('uses device family instead of viewport width', () => {
    const narrowDesktop = {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
      maxTouchPoints: 0,
    };

    expect(isDesktopPlatform(narrowDesktop)).toBe(true);
    expect(isMobile(narrowDesktop)).toBe(false);
  });

  it('rejects a Superadmin action on mobile', () => {
    vi.stubGlobal('navigator', {
      userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',
      maxTouchPoints: 5,
    });

    expect(() => assertDesktopSuperadminAvailable()).toThrow(
      DESKTOP_ONLY_SUPERADMIN_MESSAGE,
    );
    vi.unstubAllGlobals();
  });
});
