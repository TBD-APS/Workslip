import { EyeOff, ShieldCheck } from 'lucide-react';
import type { JobAuditorScopeDraft } from '../api/auditorScopeApi';
import './jobAuditorScope.css';

type JobAuditorScopeControlProps = {
  value: JobAuditorScopeDraft;
  onChange: (next: JobAuditorScopeDraft) => void;
  disabled?: boolean;
};

export function JobAuditorScopeControl({ value, onChange, disabled = false }: JobAuditorScopeControlProps) {
  const makeInternal = () => {
    onChange({ isInAuditorScope: false, reason: '' });
  };

  const makeVisible = () => {
    onChange({ isInAuditorScope: true, reason: '' });
  };

  return (
    <section
      className={`auditor-scope-card auditor-scope-card--create ${value.isInAuditorScope ? '' : 'auditor-scope-card--internal'}`}
      aria-labelledby="auditor-scope-title"
    >
      <div className="auditor-scope-card__icon" aria-hidden="true">
        {value.isInAuditorScope ? <ShieldCheck size={20} /> : <EyeOff size={20} />}
      </div>
      <div className="auditor-scope-card__body">
        <div className="auditor-scope-card__heading">
          <div>
            <span className="auditor-scope-card__eyebrow">Auditøradgang</span>
            <h2 id="auditor-scope-title">
              {value.isInAuditorScope ? 'Med i auditørvisningen' : 'Intern sag'}
            </h2>
          </div>
          <button
            className="btn btn-secondary"
            type="button"
            onClick={value.isInAuditorScope ? makeInternal : makeVisible}
            disabled={disabled}
          >
            {value.isInAuditorScope ? 'Gør intern' : 'Vis for auditør'}
          </button>
        </div>

        <p className="auditor-scope-card__description">
          {value.isInAuditorScope
            ? 'Sagen kan vises til auditør, når den også matcher auditørens faglige scope.'
            : 'Sagen oprettes som intern og holdes ude af auditørens arbejdsflade.'}
        </p>
      </div>
    </section>
  );
}
