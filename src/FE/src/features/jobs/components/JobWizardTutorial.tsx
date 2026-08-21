import { HelpCircle, X } from 'lucide-react';
import { useState } from 'react';
import { useAuth } from '../../../providers/useAuth';
import { JOB_STEPS } from './steps/jobSteps';
import './JobWizardTutorial.css';

const GUIDE_SEEN_STORAGE_KEY_PREFIX = 'workslip.job-wizard-guide-seen.v2';

type JobStepLabel = (typeof JOB_STEPS)[number]['label'];
type GuideCopy = {
  title: string;
  description: string;
  tip: string;
};

const GUIDE_COPY: Record<JobStepLabel, GuideCopy> = {
  Sagsdetaljer: {
    title: 'Start med de vigtigste oplysninger',
    description:
      'Tjek kunde, arbejdssted og de andre oplysninger om sagen. Felter med * skal være udfyldt, før du kan gå videre.',
    tip: 'Mangler der noget, så ret det her først. Tryk Næste, når oplysningerne er rigtige.',
  },
  Anlægstyper: {
    title: 'Vælg det arbejde, du har udført',
    description:
      'Vælg de anlægstyper og den arbejdstype, der passer til opgaven. Dine valg bestemmer, hvilke kontrolpunkter du får vist bagefter.',
    tip: 'Vælg kun det, der hører til denne sag. Du kan altid gå tilbage og ændre det senere.',
  },
  Kontrolpunkter: {
    title: 'Gennemgå de relevante kontrolpunkter',
    description:
      'Vælg mindst ét kontrolpunkt i hver relevant kategori. Hvis en kategori ikke gælder for arbejdet, kan du markere den som ikke relevant.',
    tip: 'Hvis du ikke kan gå videre, så tjek om der mangler et valg eller en forklaring på dette trin.',
  },
  Timesedler: {
    title: 'Registrér tid og eventuelle udlæg',
    description:
      'Tilføj den arbejdstid og de udlæg, der hører til sagen. Du kan have flere timesedler på den samme sag.',
    tip: 'Tjek at timer og udlæg er rigtige, før du går videre. Du kan gå tilbage og rette dem senere.',
  },
  Afslutning: {
    title: 'Fortæl hvordan arbejdet blev afsluttet',
    description:
      'Vælg den afslutning, der passer til arbejdet. Hvis arbejdet ikke er færdigt, skal du vælge det i stedet for at sende sagen videre som færdig.',
    tip: 'Vælg det, der passer til situationen. Du kan gå tilbage, hvis noget tidligere i sagen skal rettes.',
  },
  Attestering: {
    title: 'Tjek sagen én sidste gang',
    description:
      'Gennemgå oplysningerne og bekræft, at de er rigtige. Når du sender sagen videre, går den til gennemsyn og godkendelse.',
    tip: 'Brug Tilbage, hvis du vil ændre noget. Send først sagen videre, når alt ser rigtigt ud.',
  },
};

function getGuideSeenStorageKey(organizationId: string, userId: string) {
  return `${GUIDE_SEEN_STORAGE_KEY_PREFIX}.${organizationId}.${userId}`;
}

function shouldOpenGuideInitially(storageKey: string | null) {
  if (!storageKey) return true;

  try {
    return window.localStorage.getItem(storageKey) !== '1';
  } catch {
    return true;
  }
}

function rememberGuideSeen(storageKey: string | null) {
  if (!storageKey) return;

  try {
    window.localStorage.setItem(storageKey, '1');
  } catch {
    // UI guidance must still work when browser storage is unavailable.
  }
}

type JobWizardTutorialProps = {
  currentStep: number;
};

export function JobWizardTutorial({ currentStep }: JobWizardTutorialProps) {
  const { user } = useAuth();
  const storageKey = user ? getGuideSeenStorageKey(user.organizationId, user.id) : null;
  const [isOpen, setIsOpen] = useState(() => shouldOpenGuideInitially(storageKey));
  const stepIndex = Math.min(Math.max(currentStep, 0), JOB_STEPS.length - 1);
  const step = JOB_STEPS[stepIndex] ?? JOB_STEPS[0];
  const guide = GUIDE_COPY[step.label];
  const StepIcon = step.icon;

  const closeGuide = () => {
    setIsOpen(false);
    rememberGuideSeen(storageKey);
  };

  return (
    <div className="job-wizard-tutorial-shell">
      <button
        type="button"
        className="btn btn-secondary btn-sm job-wizard-tutorial-trigger"
        onClick={() => {
          if (isOpen) {
            closeGuide();
          } else {
            setIsOpen(true);
          }
        }}
        aria-expanded={isOpen}
        aria-controls="job-wizard-tutorial-panel"
      >
        <HelpCircle size={16} aria-hidden="true" />
        <span>{isOpen ? 'Skjul' : 'Vis hjælp'}</span>
      </button>

      {isOpen && (
        <aside
          id="job-wizard-tutorial-panel"
          className="job-wizard-tutorial"
          aria-label={`Hjælp til ${step.label}`}
          aria-live="polite"
        >
          <div className="job-wizard-tutorial-header">
            <div className="job-wizard-tutorial-icon" aria-hidden="true">
              <StepIcon size={20} />
            </div>
            <div className="job-wizard-tutorial-heading">
              <span className="job-wizard-tutorial-kicker">
                Trin {stepIndex + 1} af {JOB_STEPS.length}
              </span>
              <h3>{step.label}</h3>
            </div>
            <button
              type="button"
              className="btn-icon job-wizard-tutorial-close"
              onClick={closeGuide}
              aria-label="Skjul hjælp"
            >
              <X size={18} aria-hidden="true" />
            </button>
          </div>

          <div className="job-wizard-tutorial-copy">
            <strong>{guide.title}</strong>
            <p>{guide.description}</p>
            <p className="job-wizard-tutorial-tip">{guide.tip}</p>
          </div>

          <div className="job-wizard-tutorial-progress" aria-label="Sagens trin">
            {JOB_STEPS.map((wizardStep, index) => {
              const isActive = index === stepIndex;
              const isPast = index < stepIndex;
              return (
                <span
                  key={wizardStep.label}
                  className={`job-wizard-tutorial-progress-step${isActive ? ' is-active' : ''}${isPast ? ' is-past' : ''}`}
                  aria-current={isActive ? 'step' : undefined}
                  aria-label={isActive ? `${wizardStep.label} - du er her` : wizardStep.label}
                  title={wizardStep.label}
                >
                  <span className="job-wizard-tutorial-progress-dot" aria-hidden="true" />
                </span>
              );
            })}
          </div>

          <p className="job-wizard-tutorial-footer">
            Brug Tilbage, hvis du vil ændre noget, du allerede har udfyldt. Tryk Næste, når du er klar til at gå videre.
          </p>
        </aside>
      )}
    </div>
  );
}