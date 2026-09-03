import { AlertCircle, ArrowRight } from 'lucide-react';
import type { JobValidationIssue } from './jobValidation';
import { JOB_STEPS } from '../components/steps/jobSteps';
import '../../../components/common/ActivityFeed.css';
import '../../../components/common/NotificationsDrawer.css';
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
    <div id={id} className="notifications-list activity-feed" role="alert" aria-live="assertive">
      <div className="notifications-toolbar">
        <span>{title}</span>
        <span>{issues.length === 1 ? '1 ting skal rettes' : `${issues.length} ting skal rettes`}</span>
      </div>

      <section className="activity-section" aria-label="Valideringsfejl">
        {issues.map((issue) => (
          <div
            key={`${issue.code}-${issue.field}-${issue.message}`}
            className="activity-row notification-item"
          >
            <span className="activity-avatar activity-avatar-danger" aria-hidden="true">
              <AlertCircle size={17} />
            </span>

            <div className="activity-content">
              <div className="activity-heading">
                <strong className="activity-title">{issue.message}</strong>
              </div>
              <div className="activity-meta">
                <span className="activity-meta-item">
                  Trin {issue.step + 1} · {JOB_STEPS[issue.step]?.label ?? 'Sagen'}
                </span>
              </div>
              <div className="activity-actions">
                <button
                  type="button"
                  className="activity-action notification-action-primary"
                  onClick={() => onAction(issue)}
                  aria-label={`${issue.actionLabel}: ${issue.message}`}
                >
                  <span>{issue.actionLabel}</span>
                  <ArrowRight size={14} aria-hidden="true" />
                </button>
              </div>
            </div>
          </div>
        ))}
      </section>
    </div>
  );
}
