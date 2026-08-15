import type { JobListItemViewModel } from '../../api/generated/models';

export function getQuickJobSearchTerm(query: string): string | null {
  const trimmed = query.trim();
  if (trimmed.length < 2) return null;

  const withoutPrefix = trimmed.replace(/^(sag|job)\s*#?\s*/i, '').trim();
  return withoutPrefix.length >= 2 ? withoutPrefix : null;
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
