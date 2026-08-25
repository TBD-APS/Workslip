// @ts-expect-error Frontend compilation excludes Node typings; Vitest executes this test in Node.
import { readFileSync } from 'fs';
import { describe, expect, it } from 'vitest';

// @ts-expect-error Frontend compilation excludes Node typings; Vitest provides process at runtime.
const css = readFileSync(`${process.cwd()}/src/components/layouts/AppLayout.focus.css`, 'utf8');

describe('AppLayout mobile navigation contract', () => {
  it('drives phone navigation from a hamburger toggle and a scrim, hidden by default', () => {
    expect(css).toContain('@media (max-width: 767px)');
    // The toggle and scrim are phone-only affordances hidden at every width by default.
    expect(css).toContain('.app-nav-toggle,');
    expect(css).toContain('.mobile-nav-scrim');
    expect(css).toContain('.app-nav-toggle {');
    expect(css).toContain('display: inline-flex');
    expect(css).toContain('.app-shell.mobile-nav-open .mobile-nav-scrim');
  });

  it('keeps phone content full-width with no rail gutter', () => {
    // No persistent rail: content spans the viewport instead of being offset.
    expect(css).toContain('margin-left: 0');
    expect(css).toContain('max-width: 100%');
    expect(css).not.toContain('--mobile-nav-rail-width');
  });

  it('renders the nav as an off-canvas left drawer toggled by the open state', () => {
    expect(css).toContain('height: 100dvh');
    expect(css).toContain('flex-direction: column');
    expect(css).toContain('border-right: 1px solid var(--border)');
    expect(css).toContain('transform: translateX(-100%)');
    expect(css).toContain('.app-shell.mobile-nav-open .bottom-nav');
    expect(css).toContain('transform: translateX(0)');
  });

  it('preserves keyboard-safe hiding of the wizard action bar', () => {
    expect(css).toContain("[contenteditable]:not([contenteditable='false']):focus");
    expect(css).toContain('visibility: hidden');
    expect(css).toContain('pointer-events: none');
  });
});
