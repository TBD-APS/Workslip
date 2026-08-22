import { useMemo, useState } from 'react';
import { evaluateHelpWizard } from './evaluateHelpWizard';
import { readHelpWizardAssignment } from './readHelpWizardAssignment';
import './help-wizard.css';

export function HelpWizard() {
  const decision = useMemo(
    () => evaluateHelpWizard(readHelpWizardAssignment()),
    [],
  );
  const [open, setOpen] = useState(false);

  if (!decision.enabled) {
    return null;
  }

  return (
    <div id="help-wizard" className="help-wizard" data-testid="help-wizard">
      {open && (
        <div id="help-wizard-message" className="help-wizard-bubble" role="status">
          Det ser ud til, du opretter et job. Skal jeg hjælpe?
        </div>
      )}
      <button
        id="help-wizard-toggle"
        type="button"
        className="help-wizard-clip"
        aria-label="Hjælp"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <svg viewBox="0 0 48 64" width="40" height="54" aria-hidden="true">
          <path
            d="M18 8c-6 0-10 5-10 12v22c0 8 6 14 14 14s14-6 14-14V18c0-4-3-7-7-7s-7 3-7 7v20c0 2 1 4 4 4s4-2 4-4V22"
            fill="none"
            stroke="currentColor"
            strokeWidth="3"
            strokeLinecap="round"
          />
          <circle cx="20" cy="20" r="2.2" fill="currentColor" />
          <circle cx="28" cy="20" r="2.2" fill="currentColor" />
        </svg>
      </button>
    </div>
  );
}
