export type Platform = 'ios' | 'android' | 'desktop';

export function detectPlatform(): Platform {
  const ua = navigator.userAgent;
  if (/iPad|iPhone|iPod/.test(ua)) return 'ios';
  if (/android/i.test(ua)) return 'android';
  return 'desktop';
}

export function isMobile(): boolean {
  const p = detectPlatform();
  return p === 'ios' || p === 'android';
}
