import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const desktopCss = readFileSync(new URL('./AppLayout.desktop.css', import.meta.url), 'utf8');
const quickNavigatorCss = readFileSync(new URL('../common/QuickNavigator.css', import.meta.url), 'utf8');

describe('AppLayout search navigation visibility', () => {
  it('keeps the global search trigger visible on tablet and desktop', () => {
    expect(quickNavigatorCss).toContain('@media (min-width: 768px)');
    expect(quickNavigatorCss).toMatch(/\.quick-nav-mobile-trigger\s*\{[\s\S]*?display:\s*none;/);

    expect(desktopCss).toMatch(
      /@media \(min-width: 768px\)[\s\S]*?\.bottom-nav \.quick-nav-mobile-trigger\s*\{[\s\S]*?display:\s*flex;/,
    );
  });
});
