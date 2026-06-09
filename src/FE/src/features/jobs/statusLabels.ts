import type { JobStatus } from '../../api/generated/models/jobStatus';

const JOB_STATUS_LABELS = {
  Draft: 'Kladde',
  InReview: 'Til gennemsyn',
  Approved: 'Godkendt',
  Rejected: 'Afvist',
} as const satisfies Record<JobStatus, string>;

export function formatJobStatus<TStatus extends JobStatus>(status: TStatus): (typeof JOB_STATUS_LABELS)[TStatus];
export function formatJobStatus(status: string): string;
export function formatJobStatus(status: string): string {
  return status in JOB_STATUS_LABELS ? JOB_STATUS_LABELS[status as JobStatus] : status;
}
