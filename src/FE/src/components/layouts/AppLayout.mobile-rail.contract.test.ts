import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

const cssPath = path.resolve(__dirname, 'AppLayout.focus.css');
const css = fs.readFileSync(cssPath, 'utf8');

describe('AppLayout mobile navigation rail contract', () => {
  it('renders the phone navigation as a left rail instead of a bottom bar', () => {
    expect(css).toContain('@media (max-width: 767px)');
    expect(css).toContain('--mobile-nav-rail-width: 4.25rem');
    expect(css).toContain('width: var(--mobile-nav-rail-width)');
    expect(css).toContain('height: 100dvh');
    expect(css).toContain('flex-direction: column');
    expect(css).toContain('border-right: 1px solid var(--border)');
  });

  it('keeps phone content beside the rail and preserves keyboard-safe hiding', () => {
    expect(css).toContain('margin-left: var(--mobile-nav-rail-width)');
    expect(css).toContain('max-width: calc(100% - var(--mobile-nav-rail-width))');
    expect(css).toContain("[contenteditable]:not([contenteditable='false']):focus");
    expect(css).toContain('visibility: hidden');
    expect(css).toContain('pointer-events: none');
  });
});
