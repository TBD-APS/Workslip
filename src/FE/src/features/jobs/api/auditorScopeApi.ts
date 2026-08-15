import { customAxiosInstance } from '../../../api/fetcherOrval';

export type JobAuditorScope = {
  isInAuditorScope: boolean;
  reason?: string | null;
};

export type JobAuditorScopeDraft = {
  isInAuditorScope: boolean;
  reason: string;
};

export type SetJobAuditorScopeRequest = {
  isInAuditorScope: boolean;
  reason?: string | null;
};

export function setJobAuditorScope(jobId: string, data: SetJobAuditorScopeRequest) {
  return customAxiosInstance<JobAuditorScope>({
    url: `/api/jobs/${jobId}/auditor-scope`,
    method: 'PUT',
    data,
    skipGlobalErrorToast: true,
  });
}
