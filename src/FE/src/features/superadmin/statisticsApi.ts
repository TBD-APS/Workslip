import { apiClient } from '../../lib/axios';

export type DurationMetric = {
  averageSeconds: number | null;
  medianSeconds: number | null;
  p75Seconds: number | null;
  sampleSize: number;
};

export type WorkflowStatisticsSummary = {
  caseCount: number;
  submittedCount: number;
  approvedCount: number;
  rejectedCaseCount: number;
  rejectionEventCount: number;
  firstPassApprovalRate: number | null;
  rejectionRate: number | null;
  averageReworkLoops: number | null;
  systemUiRejectionRate: number | null;
  rejectionReasonCoverageRate: number | null;
  createdToApproved: DurationMetric;
  employeeActive: DurationMetric;
  employeeElapsed: DurationMetric;
  assignedToSubmitted: DurationMetric;
  assignedToFirstWork: DurationMetric;
  inactiveAfterStart: DurationMetric;
};

export type WorkflowTrendPoint = {
  periodStart: string;
  caseCount: number;
  createdToApprovedMedianSeconds: number | null;
  employeeActiveMedianSeconds: number | null;
  assignedToSubmittedMedianSeconds: number | null;
  firstPassApprovalRate: number | null;
  rejectionRate: number | null;
};

export type DistributionBucket = {
  key: string;
  label: string;
  count: number;
  rate: number;
};

export type RejectionReasonBucket = {
  code: string;
  label: string;
  count: number;
  rate: number;
};

export type WorkflowOrganizationSummary = {
  organizationId: string;
  organizationName: string;
  caseCount: number;
  submittedCount: number;
  approvedCount: number;
  firstPassApprovalRate: number | null;
  rejectionRate: number | null;
  createdToApproved: DurationMetric;
  employeeActive: DurationMetric;
  assignedToSubmitted: DurationMetric;
};

export type WorkflowStatisticsResponse = {
  windowDays: number;
  from: string;
  generatedAt: string;
  summary: WorkflowStatisticsSummary;
  trend: WorkflowTrendPoint[];
  employeeActiveDistribution: DistributionBucket[];
  assignedToSubmittedDistribution: DistributionBucket[];
  rejectionReasons: RejectionReasonBucket[];
  organizations: WorkflowOrganizationSummary[];
};

export const workflowStatisticsQueryKey = (days: number, organizationId?: string) =>
  ['superadmin', 'statistics', 'workflow', days, organizationId || 'all'] as const;

export async function getWorkflowStatistics(input: {
  days: number;
  organizationId?: string;
}): Promise<WorkflowStatisticsResponse> {
  return await apiClient.get('/api/superadmin/analytics/workflow-statistics', {
    params: {
      days: input.days,
      organizationId: input.organizationId || undefined,
    },
    skipGlobalErrorToast: true,
  }) as unknown as WorkflowStatisticsResponse;
}

export async function recordWorkflowActiveSegment(jobId: string, durationSeconds: number): Promise<void> {
  if (!jobId || durationSeconds < 1) return;

  try {
    await apiClient.post('/api/productivity/workflow-active-segment', {
      jobId,
      durationSeconds: Math.min(1_800, Math.max(1, Math.round(durationSeconds))),
    }, {
      skipGlobalErrorToast: true,
    });
  } catch {
    // Analytics is deliberately best-effort and must never block the job workflow.
  }
}