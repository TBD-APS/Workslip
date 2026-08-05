import { JobStatus } from '../../../api/generated/models/jobStatus';
import './JobStatusDots.css';

const STATUS_OPTIONS = [
  { status: JobStatus.Draft, label: 'Aktiv', className: 'job-status-dot--draft' },
  { status: JobStatus.InReview, label: 'Til gennemsyn', className: 'job-status-dot--in-review' },
  { status: JobStatus.Approved, label: 'Godkendt', className: 'job-status-dot--approved' },
  { status: JobStatus.Rejected, label: 'Afvist', className: 'job-status-dot--rejected' },
] as const;

type JobStatusDotsProps = {
  status: JobStatus;
  enabledStatuses?: JobStatus[];
  isPending?: boolean;
  onStatusSelect?: (status: JobStatus) => void;
};

export function JobStatusDots({
  status,
  enabledStatuses = [],
  isPending = false,
  onStatusSelect,
}: JobStatusDotsProps) {
  const currentOption = STATUS_OPTIONS.find((option) => option.status === status);

  return (
    <div className="job-status-control" aria-label="Sagsstatus">
      <div className="job-status-dots" role="group" aria-label="Vælg sagsstatus">
        {STATUS_OPTIONS.map((option) => {
          const isCurrent = option.status === status;
          const isEnabled = !isCurrent && !isPending && enabledStatuses.includes(option.status) && Boolean(onStatusSelect);
          const stateLabel = isCurrent ? 'nuværende status' : isEnabled ? 'vælg status' : 'ikke tilgængelig';

          return (
            <button
              key={option.status}
              type="button"
              className={`job-status-dot ${option.className}${isCurrent ? ' is-current' : ''}`}
              aria-label={`${option.label}, ${stateLabel}`}
              aria-current={isCurrent ? 'step' : undefined}
              aria-pressed={isCurrent}
              title={`${option.label} – ${stateLabel}`}
              disabled={!isEnabled}
              onClick={() => onStatusSelect?.(option.status)}
            />
          );
        })}
      </div>
      <span className="job-status-current-label" aria-live="polite">
        Aktuel status: <strong>{currentOption?.label ?? status}</strong>
      </span>
    </div>
  );
}
