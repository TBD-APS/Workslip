import { apiClient } from '../../lib/axios';

export async function recordCaseCreationDuration(jobIds: string[], durationSeconds: number): Promise<void> {
  if (jobIds.length === 0 || !Number.isFinite(durationSeconds) || durationSeconds < 1) return;

  await apiClient.post('/api/productivity/case-create-duration', {
    jobIds,
    durationSeconds: Math.min(86_400, Math.round(durationSeconds)),
  }, {
    skipGlobalErrorToast: true,
  });
}
