export const CLIPPY_COMMAND_EVENT = 'workslip:clippy-command';

export type ClippyReaction = 'idle' | 'attention' | 'success' | 'warning' | 'thinking';

export type ClippyCommand =
  | { type: 'go-home' }
  | { type: 'go-to'; targetId: string }
  | { type: 'point-at'; targetId: string }
  | { type: 'react'; reaction: ClippyReaction; durationMs?: number };

export type ClippyRect = {
  left: number;
  top: number;
  right: number;
  bottom: number;
  width: number;
  height: number;
};

export type ClippyViewport = {
  width: number;
  height: number;
};

export type ClippyOffset = {
  x: number;
  y: number;
};

const REACTIONS = new Set<ClippyReaction>([
  'idle',
  'attention',
  'success',
  'warning',
  'thinking',
]);
const MAX_TARGET_ID_LENGTH = 128;
const MIN_REACTION_DURATION_MS = 150;
const MAX_REACTION_DURATION_MS = 5_000;
const DEFAULT_MARGIN = 12;
const DEFAULT_GAP = 12;

function normalizeTargetId(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const targetId = value.trim();
  if (!targetId || targetId.length > MAX_TARGET_ID_LENGTH) return null;
  return targetId;
}

export function parseClippyCommand(value: unknown): ClippyCommand | null {
  if (!value || typeof value !== 'object') return null;

  const candidate = value as Record<string, unknown>;
  if (candidate.type === 'go-home') return { type: 'go-home' };

  if (candidate.type === 'go-to' || candidate.type === 'point-at') {
    const targetId = normalizeTargetId(candidate.targetId);
    return targetId ? { type: candidate.type, targetId } : null;
  }

  if (candidate.type === 'react' && REACTIONS.has(candidate.reaction as ClippyReaction)) {
    const durationMs = typeof candidate.durationMs === 'number'
      ? Math.min(
          MAX_REACTION_DURATION_MS,
          Math.max(MIN_REACTION_DURATION_MS, Math.round(candidate.durationMs)),
        )
      : undefined;

    return {
      type: 'react',
      reaction: candidate.reaction as ClippyReaction,
      ...(durationMs === undefined ? {} : { durationMs }),
    };
  }

  return null;
}

function dispatchClippyCommand(command: ClippyCommand) {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent(CLIPPY_COMMAND_EVENT, { detail: command }));
}

export const clippy = {
  goHome() {
    dispatchClippyCommand({ type: 'go-home' });
  },
  goTo(targetId: string) {
    const normalizedTargetId = normalizeTargetId(targetId);
    if (normalizedTargetId) {
      dispatchClippyCommand({ type: 'go-to', targetId: normalizedTargetId });
    }
  },
  pointAt(targetId: string) {
    const normalizedTargetId = normalizeTargetId(targetId);
    if (normalizedTargetId) {
      dispatchClippyCommand({ type: 'point-at', targetId: normalizedTargetId });
    }
  },
  react(reaction: ClippyReaction, durationMs?: number) {
    const command = parseClippyCommand({ type: 'react', reaction, durationMs });
    if (command) dispatchClippyCommand(command);
  },
} as const;

export function subscribeClippyCommands(listener: (command: ClippyCommand) => void) {
  if (typeof window === 'undefined') return () => undefined;

  const onCommand = (event: Event) => {
    const command = parseClippyCommand((event as CustomEvent<unknown>).detail);
    if (command) listener(command);
  };

  window.addEventListener(CLIPPY_COMMAND_EVENT, onCommand);
  return () => window.removeEventListener(CLIPPY_COMMAND_EVENT, onCommand);
}

export function resolveClippyTarget(targetId: string): HTMLElement | null {
  if (typeof document === 'undefined') return null;

  const byId = document.getElementById(targetId);
  if (byId) return byId;

  const candidates = document.querySelectorAll<HTMLElement>('[data-clippy-target]');
  for (const candidate of candidates) {
    if (candidate.dataset.clippyTarget === targetId) return candidate;
  }

  return null;
}

function clamp(value: number, min: number, max: number) {
  if (max < min) return min;
  return Math.min(Math.max(value, min), max);
}

export function calculateClippyTargetOffset(
  homeRect: ClippyRect,
  targetRect: ClippyRect,
  viewport: ClippyViewport,
  margin = DEFAULT_MARGIN,
  gap = DEFAULT_GAP,
): ClippyOffset | null {
  const targetIsVisible = targetRect.bottom > 0
    && targetRect.top < viewport.height
    && targetRect.right > 0
    && targetRect.left < viewport.width;

  if (!targetIsVisible) return null;

  const mascotWidth = Math.max(homeRect.width, 72);
  const mascotHeight = Math.max(homeRect.height, 78);
  const targetCenterX = targetRect.left + targetRect.width / 2;
  const targetCenterY = targetRect.top + targetRect.height / 2;

  const candidateRight = targetRect.right + gap;
  const candidateLeft = targetRect.left - mascotWidth - gap;
  const rightFits = candidateRight + mascotWidth <= viewport.width - margin;
  const leftFits = candidateLeft >= margin;
  const preferLeft = targetCenterX > viewport.width / 2;

  let desiredLeft: number;
  let desiredTop: number;

  if (preferLeft && leftFits) {
    desiredLeft = candidateLeft;
    desiredTop = targetCenterY - mascotHeight / 2;
  } else if (!preferLeft && rightFits) {
    desiredLeft = candidateRight;
    desiredTop = targetCenterY - mascotHeight / 2;
  } else if (leftFits) {
    desiredLeft = candidateLeft;
    desiredTop = targetCenterY - mascotHeight / 2;
  } else if (rightFits) {
    desiredLeft = candidateRight;
    desiredTop = targetCenterY - mascotHeight / 2;
  } else {
    const above = targetRect.top - mascotHeight - gap;
    const below = targetRect.bottom + gap;
    desiredLeft = targetCenterX - mascotWidth / 2;
    desiredTop = above >= margin ? above : below;
  }

  desiredLeft = clamp(desiredLeft, margin, viewport.width - mascotWidth - margin);
  desiredTop = clamp(desiredTop, margin, viewport.height - mascotHeight - margin);

  return {
    x: desiredLeft - homeRect.left,
    y: desiredTop - homeRect.top,
  };
}
