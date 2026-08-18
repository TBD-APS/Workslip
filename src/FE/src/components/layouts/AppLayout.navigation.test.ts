import { describe, expect, it } from 'vitest';
import desktopCss from './AppLayout.desktop.css?raw';
import quickNavigatorCss from '../common/QuickNavigator.css?raw';

describe('AppLayout search navigation visibility', () => {
  it('keeps the global search trigger visible on tablet and desktop', () => {
    expect(quickNavigatorCss).toContain('@media (min-width: 768px)');
    expect(quickNavigatorCss).toMatch(/\.quick-nav-mobile-trigger\s*\{[\s\S]*?display:\s*none;/);

    expect(desktopCss).toMatch(
      /@media \(min-width: 768px\)[\s\S]*?\.bottom-nav \.quick-nav-mobile-trigger\s*\{[\s\S]*?display:\s*flex;/,
    );
  });
});
