import { HelpCircle, X } from 'lucide-react';
import { useState } from 'react';
import { JOB_STEPS } from './steps/jobSteps';
import './JobWizardTutorial.css';

const GUIDE_SEEN_STORAGE_KEY = 'workslip.job-wizard-guide-seen.v1';

type JobStepLabel = (typeof JOB_STEPS)[number]['label'];
type GuideCopy = {
  title: string;
  description: string;
  tip: string;
};

const GUIDE_COPY: Record<JobStepLabel, GuideCopy> = {
  Sagsdetaljer: {
    title: 'Start med sagens grundoplysninger',
    description:
      'Kontrollér kunde, arbejdssted og de grundoplysninger, der beskriver sagen. De påkrævede felter skal være udfyldt, før du kan gå videre.',
    tip: 'Ret fejl her først. De næste trin bygger videre på disse oplysninger.',
  },
  Anlægstyper: {
    title: 'Vælg det arbejde, der faktisk er udført',
    description:
      'Vælg anlægstyper og arbejdstype, så de passer til opgaven. Valgene er med til at bestemme, hvilke kontrolpunkter der bliver relevante bagefter.',
    tip: 'Vælg kun det, der hører til denne sag. Det gør dokumentationen lettere at gennemgå senere.',
  },
  Kontrolpunkter: {
    title: 'Gennemgå kontrolpunkterne',
    description:
      'Tag de relevante kontrolpunkter én for én. Hvis en kategori ikke gælder for arbejdet, skal den markeres som ikke relevant med den krævede begrundelse.',
    tip: 'Guiden springer aldrig krav over. Wizardens validering afgør, hvornår du kan gå videre.',
  },
  Timesedler: {
    title: 'Registrér arbejdstid og eventuelle udlæg',
    description:
      'Tilføj mindst én timeseddel til sagen. Registrér den arbejdstid og de udlæg, der hører til det udførte arbejde.',
    tip: 'Timesedlerne bliver en del af sagens dokumentation og skal være på plads før afslutningen.',
  },
  Afslutning: {
    title: 'Beskriv hvordan arbejdet blev afsluttet',
    description:
      'Vælg den afslutningsstatus, der passer til resultatet. Hvis arbejdet ikke er færdigt, skal det fremgå tydeligt i stedet for at blive sendt videre som færdigt.',
    tip: 'Afslutningsstatus hjælper næste person med at forstå, om sagen kræver mere arbejde eller kan gå videre.',
  },
  Attestering: {
    title: 'Gennemgå, attestér og send videre',
    description:
      'Kontrollér sagen samlet og bekræft, at oplysningerne er korrekte. Når du indsender, går sagen videre i Workslips normale gennemsyns- og godkendelsesflow.',
    tip: 'Brug dette sidste trin som dit kvalitetstjek, før sagen forlader dit arbejdsflow.',
  },
};

function shouldOpenGuideInitially() {
  try {
    return window.localStorage.getItem(GUIDE_SEEN_STORAGE_KEY) !== '1';
  } catch {
    return false;
  }
}

function rememberGuideSeen() {
  try {
    window.localStorage.setItem(GUIDE_SEEN_STORAGE_KEY, '1');
  } catch {
    // UI guidance must still work when browser storage is unavailable.
  }
}

type JobWizardTutorialProps = {
  currentStep: number;
};

export function JobWizardTutorial({ currentStep }: JobWizardTutorialProps) {
  const [isOpen, setIsOpen] = useState(shouldOpenGuideInitially);
  const stepIndex = Math.min(Math.max(currentStep, 0), JOB_STEPS.length - 1);
  const step = JOB_STEPS[stepIndex] ?? JOB_STEPS[0];
  const guide = GUIDE_COPY[step.label];
  const StepIcon = step.icon;

  const closeGuide = () => {
    setIsOpen(false);
    rememberGuideSeen();
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
        <span>{isOpen ? 'Skjul guide' : 'Guide til dette trin'}</span>
      </button>

      {isOpen && (
        <aside
          id="job-wizard-tutorial-panel"
          className="job-wizard-tutorial"
          aria-label={`Guide til ${step.label}`}
          aria-live="polite"
        >
          <div className="job-wizard-tutorial-header">
            <div className="job-wizard-tutorial-icon" aria-hidden="true">
              <StepIcon size={20} />
            </div>
            <div className="job-wizard-tutorial-heading">
              <span className="job-wizard-tutorial-kicker">
                Guide · trin {stepIndex + 1} af {JOB_STEPS.length}
              </span>
              <h3>{step.label}</h3>
            </div>
            <button
              type="button"
              className="btn-icon job-wizard-tutorial-close"
              onClick={closeGuide}
              aria-label="Luk guide"
            >
              <X size={18} aria-hidden="true" />
            </button>
          </div>

          <div className="job-wizard-tutorial-copy">
            <strong>{guide.title}</strong>
            <p>{guide.description}</p>
            <p className="job-wizard-tutorial-tip">{guide.tip}</p>
          </div>

          <div className="job-wizard-tutorial-progress" aria-label="Jobforløbets trin">
            {JOB_STEPS.map((wizardStep, index) => {
              const isActive = index === stepIndex;
              const isPast = index < stepIndex;
              return (
                <span
                  key={wizardStep.label}
                  className={`job-wizard-tutorial-progress-step${isActive ? ' is-active' : ''}${isPast ? ' is-past' : ''}`}
                  aria-current={isActive ? 'step' : undefined}
                  aria-label={isActive ? `${wizardStep.label} - aktuelt trin` : wizardStep.label}
                  title={wizardStep.label}
                >
                  <span className="job-wizard-tutorial-progress-dot" aria-hidden="true" />
                </span>
              );
            })}
          </div>

          <p className="job-wizard-tutorial-footer">
            Brug wizardens normale Næste og Tilbage. Guiden følger automatisk med til det aktuelle trin.
          </p>
        </aside>
      )}
    </div>
  );
}
