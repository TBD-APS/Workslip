import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiClient } from '../../../lib/axios';
import type { MyWorksheetsMonthResponse } from '../worksheetOverviewTypes';

const { getMonthlyHoursPdfPreviewMock, downloadPdfFileMock } = vi.hoisted(() => ({
  getMonthlyHoursPdfPreviewMock: vi.fn(),
  downloadPdfFileMock: vi.fn(),
}));

vi.mock('../api/monthlyHoursPdfPreview', () => ({
  getMonthlyHoursPdfPreview: getMonthlyHoursPdfPreviewMock,
}));

vi.mock('../../../lib/pdfFile', () => ({
  downloadPdfFile: downloadPdfFileMock,
}));

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

import { AdminHoursExport } from './AdminHoursExport';

const data: MyWorksheetsMonthResponse = {
  year: 2026,
  month: 8,
  monthStart: '2026-08-01',
  monthEnd: '2026-08-31',
  totalHours: 7.5,
  outlayCount: 0,
  weeks: [
    {
      weekStart: '2026-08-03',
      weekEnd: '2026-08-09',
      totalHours: 7.5,
      outlayCount: 0,
      days: [
        {
          date: '2026-08-04',
          totalHours: 7.5,
          outlayCount: 0,
          entries: [
            {
              workDate: '2026-08-04',
              jobId: '11111111-1111-1111-1111-111111111111',
              userId: '22222222-2222-2222-2222-222222222222',
              reportNumber: 'R-42',
              customerName: 'Kunde A',
              customerAddress: null,
              hoursWorked: 7.5,
              hasOutlay: false,
              userDisplayName: 'Alex Jensen',
            },
          ],
        },
      ],
    },
  ],
};

function renderExport() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        staleTime: Number.POSITIVE_INFINITY,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <AdminHoursExport data={data} monthLabel="august 2026" />
    </QueryClientProvider>,
  );
}

describe('AdminHoursExport PDF preview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getMonthlyHoursPdfPreviewMock.mockResolvedValue({
      contentType: 'image/png',
      pages: ['AQID', 'BAUG'],
    });
    vi.mocked(apiClient.get).mockResolvedValue({ url: null });
  });

  it('renders server-generated preview pages inside Workslip without a native PDF iframe', async () => {
    renderExport();

    fireEvent.click(screen.getByRole('button', { name: 'Vis PDF' }));

    const dialog = await screen.findByRole('dialog', { name: 'PDF-preview af timer for august 2026' });
    expect(dialog).toBeInTheDocument();
    expect(getMonthlyHoursPdfPreviewMock).toHaveBeenCalledWith(2026, 8);
    expect(dialog.querySelector('iframe')).toBeNull();

    const pages = screen.getAllByRole('img');
    expect(pages).toHaveLength(2);
    expect(pages[0]).toHaveAttribute('src', 'data:image/png;base64,AQID');
    expect(pages[0]).toHaveAttribute('alt', 'Side 1 af 2');
    expect(pages[1]).toHaveAttribute('src', 'data:image/png;base64,BAUG');
  });

  it('closes the in-app preview without opening the native PDF viewer', async () => {
    const open = vi.spyOn(window, 'open');
    renderExport();

    fireEvent.click(screen.getByRole('button', { name: 'Vis PDF' }));
    await screen.findByRole('dialog', { name: 'PDF-preview af timer for august 2026' });
    fireEvent.click(screen.getByRole('button', { name: 'Luk PDF-preview' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(open).not.toHaveBeenCalled();
  });
});

describe('AdminHoursExport Power BI link', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getMonthlyHoursPdfPreviewMock.mockResolvedValue({
      contentType: 'image/png',
      pages: ['AQID'],
    });
  });

  it('shows the configured report link', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ url: 'https://app.powerbi.com/groups/me/reports/abc' });
    renderExport();

    const link = await screen.findByRole('link', { name: 'Åbn Power BI' });
    expect(link).toHaveAttribute('href', 'https://app.powerbi.com/groups/me/reports/abc');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  });

  it('stays hidden when no report link is configured', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ url: null });
    renderExport();

    await screen.findByRole('button', { name: 'CSV til Excel' });
    expect(screen.queryByRole('link', { name: 'Åbn Power BI' })).not.toBeInTheDocument();
  });
});
