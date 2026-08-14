import { useEffect, useState } from 'react';
import { CheckCircle2 } from 'lucide-react';
import { playStartupJingle, primeFeedbackAudio } from '../../lib/feedbackSounds';
import { COMPLETION_EVENT } from '../../lib/completionCelebration';

export function GamificationFeedback() {
  const [showStartup, setShowStartup] = useState(true);
  const [showCompletion, setShowCompletion] = useState(false);

  useEffect(() => {
    const startupTimer = window.setTimeout(() => setShowStartup(false), 1050);
    let startupSoundPlayed = false;

    const unlockAudio = () => {
      void primeFeedbackAudio();
      if (!startupSoundPlayed) {
        startupSoundPlayed = true;
        void playStartupJingle();
      }
    };

    const celebrate = () => {
      setShowCompletion(true);
      window.setTimeout(() => setShowCompletion(false), 1450);
    };

    window.addEventListener('pointerdown', unlockAudio, { once: true });
    window.addEventListener('keydown', unlockAudio, { once: true });
    window.addEventListener(COMPLETION_EVENT, celebrate);

    return () => {
      window.clearTimeout(startupTimer);
      window.removeEventListener('pointerdown', unlockAudio);
      window.removeEventListener('keydown', unlockAudio);
      window.removeEventListener(COMPLETION_EVENT, celebrate);
    };
  }, []);

  return (
    <>
      {showStartup && (
        <div className="workslip-startup" role="status" aria-label="Workslip starter">
          <div className="workslip-startup-inner">
            <img src="/logo.png" alt="Workslip" className="workslip-startup-logo" />
            <span className="workslip-startup-pulse" aria-hidden="true" />
          </div>
        </div>
      )}

      {showCompletion && (
        <div className="workslip-achievement" role="status" aria-live="polite">
          <div className="workslip-achievement-card">
            <span className="workslip-achievement-icon" aria-hidden="true">
              <CheckCircle2 size={44} strokeWidth={2.4} />
            </span>
            <strong>Sagen er færdig</strong>
            <span>Godt arbejde</span>
          </div>
          <span className="workslip-confetti workslip-confetti-one" aria-hidden="true" />
          <span className="workslip-confetti workslip-confetti-two" aria-hidden="true" />
          <span className="workslip-confetti workslip-confetti-three" aria-hidden="true" />
          <span className="workslip-confetti workslip-confetti-four" aria-hidden="true" />
        </div>
      )}
    </>
  );
}
