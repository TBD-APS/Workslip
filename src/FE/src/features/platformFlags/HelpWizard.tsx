import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { animate } from 'motion';
import { motion, useMotionValue, useReducedMotion } from 'motion/react';
import { findValidationTargetId, subscribeWorkslipUiFeedback } from '../../lib/uiFeedback';
import {
  calculateClippyTargetOffset,
  resolveClippyTarget,
  subscribeClippyCommands,
  type ClippyReaction,
} from './clippyController';
import { getClippyBubbleCopy } from './clippyContent';
import { evaluateHelpWizard } from './evaluateHelpWizard';
import { readHelpWizardAssignment } from './readHelpWizardAssignment';
import './help-wizard.css';

const DEFAULT_REACTION_DURATION_MS = 850;
const FEEDBACK_REACTION_DURATION_MS = 1_100;

const reactionAnimations: Record<ClippyReaction, Record<string, number | number[]>> = {
  idle: { x: 0, y: 0, rotate: 0, scale: 1 },
  attention: { rotate: [0, -5, 5, 0], scale: [1, 1.06, 1] },
  success: { y: [0, -8, 0], scale: [1, 1.08, 1] },
  warning: { x: [0, -4, 4, -3, 3, 0] },
  thinking: { rotate: [0, -3, 0, -3, 0] },
};

function GoldClippyWizard() {
  return (
    <svg
      id="help-wizard-character"
      className="clippy-wizard clippy-gold-clip"
      viewBox="0 0 92 96"
      width="68"
      height="72"
      aria-hidden="true"
    >
      <ellipse className="clippy-wizard-shadow" cx="39" cy="88" rx="27" ry="5" />

      <path
        className="clippy-wizard-clip clippy-wizard-clip-outer"
        d="M34 11c-14 0-24 11-24 25v30c0 15 12 27 27 27s27-12 27-27V35c0-12-9-21-21-21S22 23 22 35v27c0 8 6 14 14 14s14-6 14-14V40"
      />
      <path
        className="clippy-wizard-clip-highlight"
        d="M33 17c-10 0-17 8-17 19v29c0 11 8 20 19 21"
      />

      <g className="clippy-wizard-face">
        <ellipse className="clippy-wizard-eye clippy-wizard-eye-left" cx="34" cy="41" rx="2.6" ry="3.2" />
        <ellipse className="clippy-wizard-eye clippy-wizard-eye-right" cx="47" cy="41" rx="2.6" ry="3.2" />
        <path className="clippy-wizard-smile" d="M35 50c4 3 8 3 12 0" />
      </g>

      <g className="clippy-wizard-finger-gun">
        <path className="clippy-wizard-arm" d="M18 53c-6 1-10 5-12 10" />
        <path className="clippy-wizard-finger" d="m6 63 8-1" />
        <path className="clippy-wizard-thumb" d="m9 63 4 5" />
      </g>

      <path className="clippy-wizard-arm" d="M61 52c6 1 9 4 11 8" />
      <g id="help-wizard-wand" className="clippy-wizard-wand">
        <path className="clippy-wizard-wand-stick" d="m70 59 12-28" />
        <path className="clippy-wizard-spark" d="m83 23 1.7 4.6 4.6 1.7-4.6 1.7-1.7 4.6-1.7-4.6-4.6-1.7 4.6-1.7Z" />
        <circle className="clippy-wizard-dust clippy-wizard-dust-one" cx="76" cy="22" r="1.3" />
        <circle className="clippy-wizard-dust clippy-wizard-dust-two" cx="88" cy="19" r="1.1" />
        <circle className="clippy-wizard-dust clippy-wizard-dust-three" cx="90" cy="35" r="1.2" />
      </g>
    </svg>
  );
}

export function HelpWizard() {
  const decision = useMemo(
    () => evaluateHelpWizard(readHelpWizardAssignment()),
    [],
  );
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<'home' | 'target'>('home');
  const [reaction, setReaction] = useState<ClippyReaction>('idle');
  const rootRef = useRef<HTMLDivElement>(null);
  const reactionTimerRef = useRef<number | null>(null);
  const feedbackTimerRef = useRef<number | null>(null);
  const x = useMotionValue(0);
  const y = useMotionValue(0);
  const shouldReduceMotion = useReducedMotion();

  const stopReactionTimer = useCallback(() => {
    if (reactionTimerRef.current !== null) {
      window.clearTimeout(reactionTimerRef.current);
      reactionTimerRef.current = null;
    }
  }, []);

  const stopFeedbackTimer = useCallback(() => {
    if (feedbackTimerRef.current !== null) {
      window.clearTimeout(feedbackTimerRef.current);
      feedbackTimerRef.current = null;
    }
  }, []);

  const triggerReaction = useCallback((nextReaction: ClippyReaction, durationMs = DEFAULT_REACTION_DURATION_MS) => {
    stopReactionTimer();
    setReaction(nextReaction);

    if (nextReaction !== 'idle') {
      reactionTimerRef.current = window.setTimeout(() => {
        setReaction('idle');
        reactionTimerRef.current = null;
      }, durationMs);
    }
  }, [stopReactionTimer]);

  const moveToOffset = useCallback((nextX: number, nextY: number) => {
    if (shouldReduceMotion) {
      x.set(nextX);
      y.set(nextY);
      return;
    }

    animate(x, nextX, { type: 'spring', stiffness: 280, damping: 28, mass: 0.72 });
    animate(y, nextY, { type: 'spring', stiffness: 280, damping: 28, mass: 0.72 });
  }, [shouldReduceMotion, x, y]);

  const goHome = useCallback(() => {
    moveToOffset(0, 0);
    setMode('home');
  }, [moveToOffset]);

  const moveToTarget = useCallback((targetId: string, pointAt = false) => {
    const root = rootRef.current;
    const target = resolveClippyTarget(targetId);
    if (!root || !target) return;

    const currentRect = root.getBoundingClientRect();
    const currentX = x.get();
    const currentY = y.get();
    const homeRect = {
      left: currentRect.left - currentX,
      top: currentRect.top - currentY,
      right: currentRect.right - currentX,
      bottom: currentRect.bottom - currentY,
      width: currentRect.width,
      height: currentRect.height,
    };
    const offset = calculateClippyTargetOffset(
      homeRect,
      target.getBoundingClientRect(),
      { width: window.innerWidth, height: window.innerHeight },
    );

    if (!offset) return;

    moveToOffset(offset.x, offset.y);
    setMode('target');
    if (pointAt) triggerReaction('attention');
  }, [moveToOffset, triggerReaction, x, y]);

  const pointAtValidationTarget = useCallback((root: ParentNode = document) => {
    const targetId = findValidationTargetId(root);
    if (!targetId) return false;

    moveToTarget(targetId, true);
    return true;
  }, [moveToTarget]);

  const scheduleValidationCheck = useCallback((root: ParentNode = document, onMissing?: () => void) => {
    stopFeedbackTimer();
    feedbackTimerRef.current = window.setTimeout(() => {
      if (!pointAtValidationTarget(root)) onMissing?.();
      feedbackTimerRef.current = null;
    }, 0);
  }, [pointAtValidationTarget, stopFeedbackTimer]);

  useEffect(() => subscribeClippyCommands((command) => {
    switch (command.type) {
      case 'go-home':
        goHome();
        break;
      case 'go-to':
        moveToTarget(command.targetId);
        break;
      case 'point-at':
        moveToTarget(command.targetId, true);
        break;
      case 'react':
        triggerReaction(command.reaction, command.durationMs);
        break;
    }
  }), [goHome, moveToTarget, triggerReaction]);

  useEffect(() => subscribeWorkslipUiFeedback((feedback) => {
    switch (feedback.kind) {
      case 'success':
        stopFeedbackTimer();
        goHome();
        triggerReaction('success', FEEDBACK_REACTION_DURATION_MS);
        break;
      case 'error':
      case 'warning': {
        if (feedback.targetId) {
          stopFeedbackTimer();
          moveToTarget(feedback.targetId, true);
          break;
        }

        scheduleValidationCheck(document, () => {
          triggerReaction('warning', FEEDBACK_REACTION_DURATION_MS);
        });
        break;
      }
      case 'info':
        stopFeedbackTimer();
        triggerReaction('attention');
        break;
    }
  }), [goHome, moveToTarget, scheduleValidationCheck, stopFeedbackTimer, triggerReaction]);

  useEffect(() => {
    const onSubmit = (event: Event) => {
      const form = event.target instanceof HTMLFormElement ? event.target : null;
      if (!form) return;
      scheduleValidationCheck(form);
    };

    document.addEventListener('submit', onSubmit, true);
    return () => document.removeEventListener('submit', onSubmit, true);
  }, [scheduleValidationCheck]);

  useEffect(() => () => {
    stopReactionTimer();
    stopFeedbackTimer();
  }, [stopFeedbackTimer, stopReactionTimer]);

  if (!decision.enabled) {
    return null;
  }

  const characterAnimation = shouldReduceMotion
    ? reactionAnimations.idle
    : reactionAnimations[reaction];
  const bubbleCopy = getClippyBubbleCopy(window.location.pathname, reaction);

  return (
    <motion.div
      ref={rootRef}
      id="help-wizard"
      className="help-wizard"
      data-testid="help-wizard"
      data-clippy-mode={mode}
      data-clippy-reaction={reaction}
      style={{ x, y }}
    >
      {open && (
        <div id="help-wizard-message" className="help-wizard-bubble" role="status">
          <strong id="help-wizard-message-title" className="help-wizard-bubble-title">{bubbleCopy.headline}</strong>
          <span id="help-wizard-message-body" className="help-wizard-bubble-body">{bubbleCopy.body}</span>
        </div>
      )}
      <button
        id="help-wizard-toggle"
        type="button"
        className="help-wizard-toggle"
        aria-label="Hjælp"
        aria-expanded={open}
        aria-controls="help-wizard-message"
        onClick={() => setOpen((value) => !value)}
      >
        <motion.span
          className="clippy-character-stage"
          animate={characterAnimation}
          transition={{ duration: shouldReduceMotion ? 0 : 0.5, ease: 'easeOut' }}
        >
          <GoldClippyWizard />
        </motion.span>
      </button>
    </motion.div>
  );
}
