import { apiClient } from '../../lib/axios';

export const cacheStatusQueryKey = ['superadmin', 'cache-status'] as const;

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
}

export interface BackendCacheDiagnostics {
  instanceId: string;
  startedAt: string;
  lastClearedAt: string | null;
  regions: CacheRegionStatus[];
}

export interface CacheStatusResponse {
  backend: BackendCacheDiagnostics;
  vercelConfigured: boolean;
}

export interface CacheClearResponse {
  message: string;
  clearedAt: string;
  vercelConfigured: boolean;
  vercelCleared: boolean;
  warning: string | null;
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
