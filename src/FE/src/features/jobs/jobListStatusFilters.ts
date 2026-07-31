import { JobStatus } from '../../api/generated/models';
import { getSavedStatusFilter, saveStatusFilter } from '../../components/filters/StatusFilter';

export const JOB_LIST_FILTER_KEY = 'mine-jobs';

const ACTIVE_JOB_STATUSES = [JobStatus.Draft, JobStatus.Rejected] as const;
const DEFAULT_JOB_STATUSES: JobStatus[] = [...ACTIVE_JOB_STATUSES];

export const JOB_LIST_STATUS_OPTIONS = [
  { value: ACTIVE_JOB_STATUSES, label: 'Aktive og afviste' },
  { value: JobStatus.InReview, label: 'Til gennemsyn' },
  { value: JobStatus.Approved, label: 'Godkendt' },
];

export function getSavedJobListStatuses(): JobStatus[] {
  const saved = getSavedStatusFilter<JobStatus>(JOB_LIST_FILTER_KEY, DEFAULT_JOB_STATUSES);
  const hasDraft = saved.includes(JobStatus.Draft);
  const hasRejected = saved.includes(JobStatus.Rejected);

  if (hasDraft === hasRejected) {
    return saved;
  }

  const normalized = Array.from(new Set([...saved, ...ACTIVE_JOB_STATUSES]));
  saveStatusFilter(JOB_LIST_FILTER_KEY, normalized);
  return normalized;
}