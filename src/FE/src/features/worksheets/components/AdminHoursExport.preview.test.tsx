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

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({
    user: {
      organizationId: '33333333-3333-3333-3333-333333333333',
    },
  }),
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
    vi.mocked(apiClient.get).mockResolvedValue({ url: null, embedUrl: null });
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

describe('AdminHoursExport Power BI embed', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getMonthlyHoursPdfPreviewMock.mockResolvedValue({
      contentType: 'image/png',
      pages: ['AQID'],
    });
  });

  it('embeds the configured secure report and keeps the Power BI fallback link', async () => {
    const reportUrl = 'https://app.powerbi.com/groups/me/reports/11111111-2222-3333-4444-555555555555';
    const embedUrl = 'https://app.powerbi.com/reportEmbed?reportId=11111111-2222-3333-4444-555555555555&autoAuth=true';
    vi.mocked(apiClient.get).mockResolvedValue({ url: reportUrl, embedUrl });

    renderExport();

    const frame = await screen.findByTitle('Power BI timerapport');
    expect(frame).toHaveAttribute('src', embedUrl);
    expect(screen.getAllByRole('link', { name: /Power BI/ })).toHaveLength(2);
    expect(screen.getByRole('link', { name: 'Åbn i Power BI' })).toHaveAttribute('href', reportUrl);
    expect(screen.getByText('Indlæser Power BI-rapport…')).toBeInTheDocument();

    fireEvent.load(frame);
    expect(screen.queryByText('Indlæser Power BI-rapport…')).not.toBeInTheDocument();
  });

  it('shows an explicit no-config state when no report is configured', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ url: null, embedUrl: null });

    renderExport();

    expect(await screen.findByText('Power BI er ikke konfigureret endnu')).toBeInTheDocument();
    expect(screen.queryByTitle('Power BI timerapport')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Åbn i Power BI' })).not.toBeInTheDocument();
  });

  it('shows a recoverable error when report configuration cannot be fetched', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('network'));

    renderExport();

    expect(await screen.findByText('Power BI kunne ikke indlæses')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Prøv igen' })).toBeInTheDocument();
  });
});
