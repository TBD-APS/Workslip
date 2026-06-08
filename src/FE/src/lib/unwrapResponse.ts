/**
 * Unwrap an orval query/mutation response to its inner data.
 *
 * Orval's `customAxiosInstance` mutator returns `{ data, status, headers }`,
 * where `data` is the actual API response. Some API responses themselves
 * are wrapped as `{ data: T, status, ... }`. This helper collapses all
 * three possible shapes:
 *   - T
 *   - { data: T }
 *   - { data: { data: T } }
 */
export function getResponseData<T>(value: unknown): T | undefined {
  if (!value) return undefined;
  if (typeof value !== 'object') return value as T;
  if (!('data' in value)) return value as T;

  const firstData = (value as { data: T | { data: T } }).data;
  if (firstData && typeof firstData === 'object' && 'data' in firstData) {
    return (firstData as { data: T }).data;
  }

  return firstData as T;
}
