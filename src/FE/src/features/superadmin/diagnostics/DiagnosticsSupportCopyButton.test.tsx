import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DiagnosticsSupportCopyButton } from './DiagnosticsSupportCopyButton';
import { errorDiagnosticsQueryKey } from './queryKeys';
import type { ErrorDiagnosticsDashboard } from './types';

const notify = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
}));

vi.mock('../../../lib/toast', () => ({ notify }));

const dashboard: ErrorDiagnosticsDashboard = {
  isAvailable: true,
  isComplete: true,
  isStale: false,
  availabilityReason: null,
  generatedAtUtc: '2026-08-02T20:00:00.000Z',
  dataRetrievedAtUtc: '2026-08-02T20:00:00.000Z',
  summaryAvailable: true,
  itemsAvailable: true,
  telemetryHealthAvailable: true,
  hasPartialAzureResults: false,
  isTruncated: false,
  summary: {
    lastHour: 1,
    last24Hours: 2,
    last7Days: 3,
    frontendLast24Hours: 1,
    backendLast24Hours: 1,
  },
  telemetryHealth: {
    frontendLastSeenUtc: '2026-08-02T19:59:00.000Z',
    backendLastSeenUtc: '2026-08-02T19:59:30.000Z',
  },
  items: [],
};

function DiagnosticsObserver() {
  useQuery({
    queryKey: errorDiagnosticsQueryKey('24h', 'backend'),
    queryFn: () => new Promise<ErrorDiagnosticsDashboard>(() => undefined),
    retry: false,
  });
  return null;
}

function createWrapper(queryClient: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <DiagnosticsObserver />
      {children}
    </QueryClientProvider>
  );
}

describe('DiagnosticsSupportCopyButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: undefined,
    });
  });

  it('stays disabled until the active dashboard query has validated data', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(<DiagnosticsSupportCopyButton />, {
      wrapper: createWrapper(queryClient),
    });

    const button = screen.getByRole('button', { name: 'Kopiér til ChatGPT' });
    expect(button).toBeDisabled();

    await act(async () => {
      queryClient.setQueryData(errorDiagnosticsQueryKey('24h', 'backend'), dashboard);
    });

    await waitFor(() => expect(button).toBeEnabled());
  });

  it('copies the active filters and sanitized dashboard snapshot', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(errorDiagnosticsQueryKey('24h', 'backend'), dashboard);
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });

    render(<DiagnosticsSupportCopyButton />, {
      wrapper: createWrapper(queryClient),
    });

    const button = screen.getByRole('button', { name: 'Kopiér til ChatGPT' });
    await waitFor(() => expect(button).toBeEnabled());
    fireEvent.click(button);

    await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
    const snapshot = JSON.parse(writeText.mock.calls[0][0]);
    expect(snapshot).toMatchObject({
      schemaVersion: 1,
      source: 'workslip-superadmin-error-diagnostics',
      filters: { range: '24h', source: 'backend' },
      dashboard: {
        isComplete: true,
        isStale: false,
      },
    });
    expect(notify.success).toHaveBeenCalledWith('Sanitiseret diagnostik er kopieret');
  });

  it('shows a generic error when clipboard access is denied', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(errorDiagnosticsQueryKey('24h', 'backend'), dashboard);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockRejectedValue(new Error('denied with browser details')) },
    });

    render(<DiagnosticsSupportCopyButton />, {
      wrapper: createWrapper(queryClient),
    });

    const button = screen.getByRole('button', { name: 'Kopiér til ChatGPT' });
    await waitFor(() => expect(button).toBeEnabled());
    fireEvent.click(button);

    await waitFor(() => expect(notify.error).toHaveBeenCalledWith(
      'Diagnostikken kunne ikke kopieres. Prøv igen fra en sikker browserkontekst.',
    ));
    expect(notify.success).not.toHaveBeenCalled();
  });
});
