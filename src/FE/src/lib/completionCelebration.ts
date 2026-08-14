import { playCompletionJingle } from './feedbackSounds';

export const COMPLETION_EVENT = 'workslip:completion-celebration';

export function triggerCompletionCelebration() {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new Event(COMPLETION_EVENT));
  void playCompletionJingle();
}
