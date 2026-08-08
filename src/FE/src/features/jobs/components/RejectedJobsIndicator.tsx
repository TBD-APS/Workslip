import { useQuery } from '@tanstack/react-query';
import { AlertCircle } from 'lucide-react';
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';
import './RejectedJobsIndicator.css';

type JobListResponse = {
  items: JobListItemViewModel[];
  totalCount: number;
};

type RejectedJobsIndicatorProps = {
  isAdmin: boolean;
  userId?: string;
  onSelect: () => void;
};

async function getRejectedJobsCount(isAdmin: boolean): Promise<number> {
  if (isAdmin) {
    const response = await apiClient.get('/api/jobs', {
      params: {
        status: [JobStatus.Rejected],
        limit: 1,
        offset: 0,
      },
    }) as JobListResponse;

    return response.totalCount;
  }

  const assignedJobs = await apiClient.get('/api/jobs/my-assigned') as JobListItemViewModel[];
  return assignedJobs.filter((job) => job.status === JobStatus.Rejected).length;
}

export function RejectedJobsIndicator({ isAdmin, userId, onSelect }: RejectedJobsIndicatorProps) {
  const canLoad = isAdmin || Boolean(userId);
  const { data: rejectedCount = 0 } = useQuery({
    queryKey: ['/api/jobs', 'rejected-count', isAdmin ? 'organization' : userId],
    queryFn: () => getRejectedJobsCount(isAdmin),
    enabled: canLoad,
  });

  if (rejectedCount <= 0) {
    return null;
  }

  const caseLabel = rejectedCount === 1 ? '1 afvist sag' : `${rejectedCount} afviste sager`;

  return (
    <button
      type="button"
      className="rejected-jobs-indicator"
      onClick={onSelect}
      aria-label={`Vis ${caseLabel}`}
      aria-live="polite"
    >
      <AlertCircle size={16} aria-hidden="true" />
      <span>Afvist</span>
      <span className="rejected-jobs-indicator-count" aria-hidden="true">
        {rejectedCount > 99 ? '99+' : rejectedCount}
      </span>
    </button>
  );
}
