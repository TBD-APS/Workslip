import type { JobListItemViewModel } from '../../api/generated/models';

const JOB_INTENT_PREFIX = /^(sag|job)\b/i;
const CUSTOMER_INTENT_PREFIX = /^kunde\b/i;
const MIN_REMOTE_SEARCH_LENGTH = 2;

function normalizeSearchTerm(query: string): string {
  return query.trim();
}

function stripIntentPrefix(query: string, prefix: RegExp): string {
  return query.replace(prefix, '').replace(/^\s*#?\s*/, '').trim();
}

export function getQuickJobSearchTerm(query: string): string | null {
  const trimmed = normalizeSearchTerm(query);
  if (!trimmed || CUSTOMER_INTENT_PREFIX.test(trimmed)) return null;

  const term = JOB_INTENT_PREFIX.test(trimmed)
    ? stripIntentPrefix(trimmed, JOB_INTENT_PREFIX)
    : trimmed;

  return term.length >= MIN_REMOTE_SEARCH_LENGTH ? term : null;
}

export function getCustomerSearchTerm(query: string): string | null {
  const trimmed = normalizeSearchTerm(query);
  if (!trimmed || JOB_INTENT_PREFIX.test(trimmed)) return null;

  const term = CUSTOMER_INTENT_PREFIX.test(trimmed)
    ? stripIntentPrefix(trimmed, CUSTOMER_INTENT_PREFIX)
    : trimmed;

  return term.length >= MIN_REMOTE_SEARCH_LENGTH ? term : null;
}

export function filterQuickNavigationJobs(
  jobs: JobListItemViewModel[],
  canViewAllJobs: boolean,
  currentUserId?: string,
): JobListItemViewModel[] {
  if (canViewAllJobs) return jobs;
  if (!currentUserId) return [];

  return jobs.filter((job) =>
    job.assignedUsers.some((assignedUser) => assignedUser.id === currentUserId),
  );
}
