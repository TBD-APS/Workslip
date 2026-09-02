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
  reopenedCount?: number;
  recentJobs: {
    id: string;
    reportNumber: string | null;
    status: string;
    customer?: { name?: string | null } | null;
    customerName?: string | null;
    updatedAt: string;
  }[];
};

export type LeaderEconomicsSummary = {
  providerId: string;
  providerDisplayName: string;
  documentCount: number;
  invoiceCount: number;
  receiptCount: number;
  totalAmount: number;
  averageAmount: number;
  recentDocuments: Array<{
    documentId: string;
    documentNumber: string;
    type: string;
    amount: number;
    date: string;
    status: string;
    externalLink: string;
  }>;
};

export const leaderAnalysisQueryKey = ['leader-analysis', 'summary'] as const;
export const leaderEconomicsQueryKey = ['leader-analysis', 'economics'] as const;

export async function fetchLeaderEconomicsSummary(): Promise<LeaderEconomicsSummary> {
  const data = (await apiClient.get('/api/leader-analysis/economics/summary')) as unknown as LeaderEconomicsSummary;
  return data;
}

export async function fetchLeaderAnalysisSummary(): Promise<LeaderAnalysisSummary> {
  const overview = (await apiClient.get('/api/jobs/overview')) as unknown as JobOverviewResponse;

  const activeCount = overview.activeCount ?? 0;
  const inReviewCount = overview.inReviewCount ?? 0;
  const approvedCount = overview.approvedCount ?? 0;
  const rejectedCount = overview.rejectedCount ?? 0;
  const reopenedCount = (overview as { reopenedCount?: number }).reopenedCount ?? 0;
  const totalCount = activeCount + inReviewCount + approvedCount + rejectedCount + reopenedCount;
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
