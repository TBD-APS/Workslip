import { AlertCircle, ArrowRight } from 'lucide-react';
import type { JobValidationIssue } from './jobValidation';
import './ActionableValidationSummary.css';

type ActionableValidationSummaryProps = {
  issues: JobValidationIssue[];
  onAction: (issue: JobValidationIssue) => void;
  id?: string;
  title?: string;
};

export function ActionableValidationSummary({
  issues,
  onAction,
  id = 'job-validation-summary',
  title = 'Sagen mangler oplysninger før attestering',
}: ActionableValidationSummaryProps) {
  if (issues.length === 0) return null;

  return (
    <div id={id} className="validation-error actionable-validation-summary" role="alert">
      <AlertCircle size={18} aria-hidden="true" />
      <div className="actionable-validation-content">
        <span className="attestation-validation-title">{title}:</span>
        <ul className="actionable-validation-list">
          {issues.map((issue) => (
            <li key={`${issue.code}-${issue.field}-${issue.message}`} className="actionable-validation-item">
              <span className="actionable-validation-message">{issue.message}</span>
              <button
                type="button"
                className="btn btn-sm btn-secondary actionable-validation-action"
                onClick={() => onAction(issue)}
                aria-label={`${issue.actionLabel}: ${issue.message}`}
              >
                <span>{issue.actionLabel}</span>
                <ArrowRight size={14} aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
