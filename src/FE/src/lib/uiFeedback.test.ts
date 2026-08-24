import { describe, expect, it, vi } from 'vitest';
import {
  emitWorkslipUiFeedback,
  findValidationTargetId,
  parseWorkslipUiFeedback,
  subscribeWorkslipUiFeedback,
} from './uiFeedback';

describe('uiFeedback', () => {
  it('parses only bounded Workslip feedback events', () => {
    expect(parseWorkslipUiFeedback({ kind: 'success' })).toEqual({ kind: 'success' });
    expect(parseWorkslipUiFeedback({ kind: 'error', targetId: ' customer-name ' })).toEqual({
      kind: 'error',
      targetId: 'customer-name',
    });
    expect(parseWorkslipUiFeedback({ kind: 'unknown' })).toBeNull();
    expect(parseWorkslipUiFeedback(null)).toBeNull();
  });

  it('publishes feedback without exposing toast copy', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeWorkslipUiFeedback(listener);

    emitWorkslipUiFeedback({ kind: 'warning', targetId: 'save-button' });

    expect(listener).toHaveBeenCalledWith({ kind: 'warning', targetId: 'save-button' });
    unsubscribe();
  });

  it('finds an explicitly invalid field using a stable Clippy target', () => {
    const form = document.createElement('form');
    const input = document.createElement('input');
    input.id = 'email-field';
    input.setAttribute('aria-invalid', 'true');
    form.appendChild(input);

    expect(findValidationTargetId(form)).toBe('email-field');

    input.dataset.clippyTarget = 'preferred-email-target';
    expect(findValidationTargetId(form)).toBe('preferred-email-target');
  });

  it('ignores validation markers that do not have a stable target id', () => {
    const form = document.createElement('form');
    const input = document.createElement('input');
    input.className = 'form-input-invalid';
    form.appendChild(input);

    expect(findValidationTargetId(form)).toBeNull();
  });
});
