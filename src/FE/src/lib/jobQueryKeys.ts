import type { QueryKey } from '@tanstack/react-query';

const JOB_API_PATH = '/api/jobs';

// The generated single-job query key is `['/api/jobs/{id}']` (one element),
// so it does NOT prefix-match the `['/api/jobs']` family default used for
// polling and invalidation. When a push receipt or mutation should refresh
// the open app's job views, match both the list family and every cached
// detail query with this predicate.
export function isJobFamilyQueryKey(queryKey: QueryKey): boolean {
  const [first] = queryKey;
  return first === JOB_API_PATH
    || (typeof first === 'string' && first.startsWith(`${JOB_API_PATH}/`));
}
