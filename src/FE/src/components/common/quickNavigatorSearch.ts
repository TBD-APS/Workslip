import type { JobListItemViewModel } from '../../api/generated/models';

export function getQuickJobSearchTerm(query: string): string | null {
  const trimmed = query.trim();
  const hasJobIntent = /^(sag|job)\b/i.test(trimmed) || /^\d+$/.test(trimmed);
  if (!hasJobIntent) return null;

  const term = trimmed.replace(/^(sag|job)\s*#?\s*/i, '').trim();
  return term.length >= 2 ? term : null;
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
