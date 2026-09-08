import { describe, expect, it } from 'vitest';
import { calculateEconomicsKpis } from './AdminPowerBiJobStatusChart';

describe('calculateEconomicsKpis', () => {
  it('separates priced and unpriced time without inventing profit', () => {
    const result = calculateEconomicsKpis([
      { userId: 'u1', hours: 2, billableAmount: 1800 },
      { userId: 'u1', hours: 1, billableAmount: null },
      { userId: 'u2', hours: 3, billableAmount: 2700 },
    ]);

    expect(result.totalHours).toBe(6);
    expect(result.pricedHours).toBe(5);
    expect(result.unpricedHours).toBe(1);
    expect(result.totalBillable).toBe(4500);
    expect(result.pricingCoverage).toBeCloseTo(5 / 6);
    expect(result.averageBillableRate).toBe(900);
  });

  it('returns stable zero values when there is no economic data', () => {
    expect(calculateEconomicsKpis([])).toEqual({
      totalHours: 0,
      pricedHours: 0,
      unpricedHours: 0,
      totalBillable: 0,
      pricingCoverage: 0,
      averageBillableRate: 0,
    });
  });
});
