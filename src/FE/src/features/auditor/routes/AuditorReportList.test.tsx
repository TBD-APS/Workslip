import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuditorReportList } from './AuditorReportList';

const mockOrganizations = vi.fn();
const mockReports = vi.fn();

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api');
  return {
    ...actual,
    getAuditorOrganizations: (...args: unknown[]) => mockOrganizations(...args),
    getAuditorReports: (...args: unknown[]) => mockReports(...args),
  };
});

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AuditorReportList />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AuditorReportList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockReports.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('shows a fail-closed empty state when the auditor has no active assignments', async () => {
    mockOrganizations.mockResolvedValue([]);

    renderWithProviders();

    await waitFor(() => expect(screen.getByTestId('auditor-no-assignments')).toBeInTheDocument());
    expect(mockReports).not.toHaveBeenCalled();
    expect(screen.getByText('Ingen aktive tilknytninger')).toBeInTheDocument();
  });

  it('loads reports only for the selected assigned organization', async () => {
    mockOrganizations.mockResolvedValue([
      {
        assignmentId: 'assignment-1',
        organizationId: 'org-1',
        organizationName: 'VVS Test A/S',
        cvr: '12345678',
        authorizationArea: 'VVS',
        activeFrom: '2026-09-01T00:00:00Z',
        activeUntil: null,
        openFindings: 1,
        lastActivityAt: null,
      },
    ]);
    mockReports.mockResolvedValue({
      totalCount: 1,
      items: [
        {
          id: 'job-1',
          reportNumber: '1042',
          customerName: 'Testkunde',
          address: 'Testvej 1',
          status: 'Approved',
          installationTypes: 'Vand',
          totalHours: 4,
          assignedUsers: 'Montør A',
          submittedBy: 'Admin A',
          reportDate: '2026-09-29T00:00:00Z',
          submittedAt: '2026-09-29T12:00:00Z',
          updatedAt: '2026-09-29T12:00:00Z',
          openFindings: 1,
        },
      ],
    });

    renderWithProviders();

    await waitFor(() => expect(mockReports).toHaveBeenCalledWith('org-1', expect.objectContaining({ limit: 100, offset: 0 })));
    expect(await screen.findAllByText('Testkunde')).not.toHaveLength(0);
    expect(screen.getByRole('heading', { name: 'VVS Test A/S' })).toBeInTheDocument();
    expect(screen.getByTestId('auditor-report-table')).toBeInTheDocument();
  });
});
