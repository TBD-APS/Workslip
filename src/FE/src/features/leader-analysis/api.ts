import { apiClient } from '../../lib/axios';

export type LeaderAnalysisSummary = {
  activeCount: number;
  inReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  totalCount: number;
  approvalRate: number | null;
  rejectionRate: number | null;
  recentJobs: {
    id: string;
    reportNumber: string | null;
    status: string;
    customerName: string | null;
    updatedAt: string;
  }[];
};

type JobOverviewResponse = {
  activeCount: number;
  inReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  recentJobs: {
    id: string;
    reportNumber: string | null;
    status: string;
    customer?: { name?: string | null } | null;
    customerName?: string | null;
    updatedAt: string;
  }[];
};

export const leaderAnalysisQueryKey = ['leader-analysis', 'summary'] as const;

export async function fetchLeaderAnalysisSummary(): Promise<LeaderAnalysisSummary> {
  const overview = (await apiClient.get('/api/jobs/overview')) as unknown as JobOverviewResponse;

  const activeCount = overview.activeCount ?? 0;
  const inReviewCount = overview.inReviewCount ?? 0;
  const approvedCount = overview.approvedCount ?? 0;
  const rejectedCount = overview.rejectedCount ?? 0;
  const totalCount = activeCount + inReviewCount + approvedCount + rejectedCount;
  const decidedCount = approvedCount + rejectedCount;
  const approvalRate = decidedCount > 0 ? approvedCount / decidedCount : null;
  const rejectionRate = decidedCount > 0 ? rejectedCount / decidedCount : null;

  return {
    activeCount,
    inReviewCount,
    approvedCount,
    rejectedCount,
    totalCount,
    approvalRate,
    rejectionRate,
    recentJobs: (overview.recentJobs ?? []).slice(0, 6).map((job) => ({
      id: job.id,
      reportNumber: job.reportNumber ?? null,
      status: job.status,
      customerName: job.customer?.name ?? (job as { customerName?: string | null }).customerName ?? null,
      updatedAt: job.updatedAt,
    })),
  };
}
