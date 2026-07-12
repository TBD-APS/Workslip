import type { JobStatus } from '../../api/generated/models/jobStatus';

const JOB_STATUS_LABELS: Record<JobStatus, string> = {
  Draft: 'Aktiv',
  InReview: 'Til gennemsyn',
  Approved: 'Godkendt',
  Rejected: 'Afvist',
} as const;

export function formatJobStatus<TStatus extends JobStatus>(status: TStatus): (typeof JOB_STATUS_LABELS)[TStatus];
export function formatJobStatus(status: string): string;
export function formatJobStatus(status: string): string {
  return status in JOB_STATUS_LABELS ? JOB_STATUS_LABELS[status as JobStatus] : status;
}

const JOB_TYPE_LABELS: Record<string, string> = {
  KLS: '4v05',
  Diverse: 'Diverse',
  Unknown: 'Unknown'
};

export function formatJobType(jobType: string): string {
  return JOB_TYPE_LABELS[jobType] ?? jobType;
}
