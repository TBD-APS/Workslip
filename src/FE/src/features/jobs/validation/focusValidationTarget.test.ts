import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { focusValidationTarget } from './focusValidationTarget';

describe('focusValidationTarget', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    document.body.innerHTML = '<div class="app-shell"></div>';
  });

  afterEach(() => {
    vi.useRealTimers();
    document.body.innerHTML = '';
  });

  it('waits for a late-mounted target instead of jumping the app shell to the top', () => {
    const shell = document.querySelector<HTMLElement>('.app-shell')!;
    shell.scrollTo = vi.fn();

    expect(focusValidationTarget('late-target')).toBe(false);
    expect(shell.scrollTo).not.toHaveBeenCalled();

    const target = document.createElement('div');
    target.id = 'late-target';
    target.scrollIntoView = vi.fn();
    const button = document.createElement('button');
    button.focus = vi.fn();
    target.append(button);
    document.body.append(target);

    vi.advanceTimersByTime(50);

    expect(target.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'center' });
    expect(button.focus).toHaveBeenCalledWith({ preventScroll: true });
    expect(shell.scrollTo).not.toHaveBeenCalled();
  });

  it('does not move the page when the target never appears', () => {
    const shell = document.querySelector<HTMLElement>('.app-shell')!;
    shell.scrollTo = vi.fn();

    focusValidationTarget('missing-target');
    vi.runAllTimers();

    expect(shell.scrollTo).not.toHaveBeenCalled();
  });
});
