import { useQueryClient } from '@tanstack/react-query';
import type { UserBillingRateResponse } from '../../../api/generated/models';
import {
  getGetApiJobCostingUsersIdRateQueryKey,
  useGetApiJobCostingUsersIdRate,
  usePatchApiJobCostingUsersIdRate,
} from '../../../api/generated/job-costing/job-costing';
import { notify } from '../../../lib/toast';

export function normalizeBillableHourlyRate(value: number | string | null | undefined): number | null {
  if (value == null || value === '') return null;
  const parsed = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function formatBillableHourlyRate(value: number | string | null | undefined): string {
  const normalized = normalizeBillableHourlyRate(value);
  if (normalized == null) return 'Ikke angivet';

  return `${normalized.toLocaleString('da-DK', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} kr./time`;
}

export function useUserBillingRate(userId: string) {
  return useGetApiJobCostingUsersIdRate(userId, {
    query: {
      staleTime: 60_000,
    },
  });
}

export function useUpdateUserBillingRate(userId: string) {
  const queryClient = useQueryClient();

  return usePatchApiJobCostingUsersIdRate({
    mutation: {
      onSuccess: (_, variables) => {
        const nextRate = variables.data.billableHourlyRate ?? null;

        queryClient.setQueryData<UserBillingRateResponse>(
          getGetApiJobCostingUsersIdRateQueryKey(userId),
          { userId, billableHourlyRate: nextRate },
        );

        notify.success('Timepris er opdateret');
      },
      onError: () => notify.error('Timeprisen kunne ikke gemmes'),
    },
  });
}
