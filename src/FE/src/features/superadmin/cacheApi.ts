import { apiClient } from '../../lib/axios';
import type {
  CacheClearScope,
  CacheTier,
  DistributedCacheSnapshot,
} from '../../api/generated/models';

export const cacheStatusQueryKey = ['superadmin', 'cache-status'] as const;

/**
 * The counters mirror the generated `CacheRegionSnapshot`, but narrowed to `number`:
 * the OpenAPI document allows int64 and double to arrive as a JSON string, so the
 * generated model types them as `number | string`, and this screen sums and formats
 * them. Field names, enums and nullability all follow the generated contract.
 */
export interface CacheRegionStatus {
  name: string;
  type: string;
  ttlSeconds: number;
  hits: number;
  misses: number;
  sets: number;
  invalidations: number;
  failures: number;
  loads: number;
  averageLoadDurationMs: number;
  lastActivityAt: string | null;
  /** Which levels serve this region: in-process only, or in-process in front of a shared L2. */
  tier: CacheTier;
  /** How far a cache clear reaches for this region. */
  clearScope: CacheClearScope;
}

export interface BackendCacheDiagnostics {
  instanceId: string;
  startedAt: string;
  lastClearedAt: string | null;
  regions: CacheRegionStatus[];
}

export interface CacheStatusResponse {
  backend: BackendCacheDiagnostics;
  /** Whether a distributed cache (the HybridCache L2) is registered, and whether it answered. */
  distributed: DistributedCacheSnapshot;
  /** The widest scope a single clear reaches, across all regions. */
  clearScope: CacheClearScope;
  /**
   * Always false: a clear invalidates the serving process and, when configured, the
   * shared tier, but never the in-process caches of the other replicas.
   */
  clearReachesEveryReplica: boolean;
}

export interface CacheClearResponse {
  message: string;
  clearedAt: string;
  /** The API instance that served the clear — the only process whose caches were emptied. */
  instanceId: string;
  scope: CacheClearScope;
  reachedEveryReplica: boolean;
  /** Whether the shared tier was successfully marked invalid. */
  distributedTierCleared: boolean;
  distributed: DistributedCacheSnapshot;
}

/**
 * The closed set of failure reasons the status and clear endpoints may return, and
 * their Danish copy. The backend constructs the reason from this exact vocabulary —
 * `DistributedCacheProbe.FailureReasons` in
 * `src/BE/WorkslipApi/Workslip.Infrastructure/Diagnostics/DistributedCacheProbe.cs` —
 * because a StackExchange.Redis message carries the cache's host name and the screen
 * must never render an address. Changing a key here means changing that class too.
 */
const distributedFailureCopy: Record<string, string> = {
  'Timed out waiting for the distributed cache.':
    'Den delte cache svarede ikke inden for tidsgrænsen.',
  'The distributed cache did not accept a connection.':
    'Forbindelsen til den delte cache blev afvist.',
  'The distributed cache host name could not be resolved.':
    'Værtsnavnet på den delte cache kunne ikke slås op.',
  'The distributed cache rejected the connection credentials.':
    'Den delte cache afviste loginoplysningerne.',
};

/** Prefix: the backend may append the exception type in parentheses. */
const unexpectedDistributedFailure = 'The distributed cache returned an unexpected error.';

/**
 * Danish copy for a reported failure reason. An unrecognised value is described
 * generically and never rendered: the field is a closed vocabulary, so anything else
 * is either a version skew or text that should not have reached the browser.
 */
export function describeDistributedFailure(error: string | null | undefined): string | null {
  if (!error) return null;
  if (distributedFailureCopy[error]) return distributedFailureCopy[error];
  if (error.startsWith(unexpectedDistributedFailure)) {
    return 'Den delte cache returnerede en uventet fejl.';
  }
  return 'Årsagen blev ikke oplyst i en genkendt form.';
}

export async function getCacheStatus(): Promise<CacheStatusResponse> {
  return await apiClient.get('/api/superadmin/cache/status', {
    skipGlobalErrorToast: true,
  }) as unknown as CacheStatusResponse;
}

export async function clearCaches(): Promise<CacheClearResponse> {
  return await apiClient.post('/api/superadmin/cache/clear', undefined, {
    skipGlobalErrorToast: true,
  }) as unknown as CacheClearResponse;
}
