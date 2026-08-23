export const WORKSLIP_UI_FEEDBACK_EVENT = 'workslip:ui-feedback';

export type WorkslipUiFeedbackKind = 'success' | 'error' | 'warning' | 'info';

export type WorkslipUiFeedback = {
  kind: WorkslipUiFeedbackKind;
  targetId?: string;
};

const FEEDBACK_KINDS = new Set<WorkslipUiFeedbackKind>([
  'success',
  'error',
  'warning',
  'info',
]);
const MAX_TARGET_ID_LENGTH = 128;
const VALIDATION_TARGET_SELECTOR = [
  '[aria-invalid="true"]',
  '[data-invalid="true"]',
  '.form-input-invalid',
].join(',');

function normalizeTargetId(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const targetId = value.trim();
  if (!targetId || targetId.length > MAX_TARGET_ID_LENGTH) return null;
  return targetId;
}

export function parseWorkslipUiFeedback(value: unknown): WorkslipUiFeedback | null {
  if (!value || typeof value !== 'object') return null;

  const candidate = value as Record<string, unknown>;
  if (!FEEDBACK_KINDS.has(candidate.kind as WorkslipUiFeedbackKind)) return null;

  const targetId = normalizeTargetId(candidate.targetId);
  return {
    kind: candidate.kind as WorkslipUiFeedbackKind,
    ...(targetId ? { targetId } : {}),
  };
}

export function emitWorkslipUiFeedback(feedback: WorkslipUiFeedback): void {
  if (typeof window === 'undefined') return;

  const parsed = parseWorkslipUiFeedback(feedback);
  if (!parsed) return;

  window.dispatchEvent(new CustomEvent(WORKSLIP_UI_FEEDBACK_EVENT, { detail: parsed }));
}

export function subscribeWorkslipUiFeedback(
  listener: (feedback: WorkslipUiFeedback) => void,
): () => void {
  if (typeof window === 'undefined') return () => undefined;

  const onFeedback = (event: Event) => {
    const feedback = parseWorkslipUiFeedback((event as CustomEvent<unknown>).detail);
    if (feedback) listener(feedback);
  };

  window.addEventListener(WORKSLIP_UI_FEEDBACK_EVENT, onFeedback);
  return () => window.removeEventListener(WORKSLIP_UI_FEEDBACK_EVENT, onFeedback);
}

export function findValidationTargetId(root: ParentNode = document): string | null {
  const target = root.querySelector<HTMLElement>(VALIDATION_TARGET_SELECTOR);
  if (!target) return null;

  return normalizeTargetId(target.dataset.clippyTarget)
    ?? normalizeTargetId(target.id);
}
