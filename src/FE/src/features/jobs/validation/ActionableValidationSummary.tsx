import { AlertCircle, ArrowRight } from 'lucide-react';
import type { JobValidationIssue } from './jobValidation';
import './ActionableValidationSummary.css';

type ActionableValidationSummaryProps = {
  issues: JobValidationIssue[];
  onAction: (issue: JobValidationIssue) => void;
  id?: string;
  title?: string;
};

const STEP_LABELS = [
  'Stamdata',
  'Anlægstyper',
  'Kontrolpunkter',
  'Timesedler',
  'Afslutning',
  'Attestering',
] as const;

export function ActionableValidationSummary({
  issues,
  onAction,
  id = 'job-validation-summary',
  title = 'Sagen mangler oplysninger før attestering',
}: ActionableValidationSummaryProps) {
  if (issues.length === 0) return null;

  return (
    <div id={id} className="validation-error actionable-validation-summary" role="alert" aria-live="assertive">
      <div className="actionable-validation-heading">
        <span className="actionable-validation-icon" aria-hidden="true">
          <AlertCircle size={22} />
        </span>
        <div className="actionable-validation-heading-copy">
          <strong className="actionable-validation-title">{title}</strong>
          <span className="actionable-validation-count">
            {issues.length === 1 ? '1 ting skal rettes' : `${issues.length} ting skal rettes`}
          </span>
        </div>
      </div>

      <ul className="actionable-validation-list">
        {issues.map((issue) => (
          <li key={`${issue.code}-${issue.field}-${issue.message}`} className="actionable-validation-item">
            <div className="actionable-validation-copy">
              <span className="actionable-validation-step">
                Trin {issue.step + 1} · {STEP_LABELS[issue.step] ?? 'Sagen'}
              </span>
              <span className="actionable-validation-message">{issue.message}</span>
            </div>
            <button
              type="button"
              className="btn btn-sm btn-secondary actionable-validation-action"
              onClick={() => onAction(issue)}
              aria-label={`${issue.actionLabel}: ${issue.message}`}
            >
              <span>{issue.actionLabel}</span>
              <ArrowRight size={15} aria-hidden="true" />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
