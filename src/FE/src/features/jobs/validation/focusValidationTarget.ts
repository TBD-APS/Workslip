const VALIDATION_TARGET_RETRY_DELAY_MS = 50;
const VALIDATION_TARGET_MAX_ATTEMPTS = 10;

function findValidationTarget(targetId: string): HTMLElement | null {
  return document.getElementById(targetId)
    ?? document.querySelector<HTMLElement>(`[data-field-error="${targetId}"]`);
}

export function focusValidationTarget(
  targetId: string,
  attemptsRemaining = VALIDATION_TARGET_MAX_ATTEMPTS,
): boolean {
  const target = findValidationTarget(targetId);

  if (!target) {
    if (attemptsRemaining > 0) {
      window.setTimeout(
        () => focusValidationTarget(targetId, attemptsRemaining - 1),
        VALIDATION_TARGET_RETRY_DELAY_MS,
      );
    }

    return false;
  }

  target.scrollIntoView({ behavior: 'smooth', block: 'center' });
  target.classList.add('validation-focus-target');

  const focusable = target.matches('input, textarea, button, select, [tabindex]')
    ? target
    : target.querySelector<HTMLElement>('input, textarea, button, select, [tabindex]');

  focusable?.focus({ preventScroll: true });
  window.setTimeout(() => target.classList.remove('validation-focus-target'), 1500);
  return true;
}
