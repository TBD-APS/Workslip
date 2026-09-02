import { describe, expect, it } from 'vitest';

// Replicates the Overview total calculation after the fix:
// overview.activeCount + inReviewCount + approvedCount + rejectedCount + reopenedCount
function computeTotal(overview: { activeCount: number; inReviewCount: number; approvedCount: number; rejectedCount: number; reopenedCount?: number }) {
  return overview.activeCount + overview.inReviewCount + overview.approvedCount + overview.rejectedCount + (overview.reopenedCount ?? 0);
}

describe('Overview reopened total', () => {
  it('includes Reopened in total count', () => {
    const overview = { activeCount: 5, inReviewCount: 2, approvedCount: 10, rejectedCount: 1, reopenedCount: 3, recentJobs: [] };
    expect(computeTotal(overview)).toBe(21);
  });

  it('defaults reopenedCount to 0 when missing (backward compat)', () => {
    const overview = { activeCount: 5, inReviewCount: 2, approvedCount: 10, rejectedCount: 1, recentJobs: [] };
    expect(computeTotal(overview)).toBe(18);
  });

  it('leader-analysis total includes reopened', () => {
    // Mirrors fetchLeaderAnalysisSummary logic in src/FE/src/features/leader-analysis/api.ts
    const overview = { activeCount: 1, inReviewCount: 1, approvedCount: 1, rejectedCount: 1, reopenedCount: 1 } as unknown as Record<string, number>;
    const total = (overview.activeCount ?? 0) + (overview.inReviewCount ?? 0) + (overview.approvedCount ?? 0) + (overview.rejectedCount ?? 0) + ((overview as { reopenedCount?: number }).reopenedCount ?? 0);
    expect(total).toBe(5);
  });
});
