import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
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

describe('AdminHoursExport PDF preview', () => {
  beforeEach(() => {
    getMonthlyHoursPdfPreviewMock.mockReset();
    downloadPdfFileMock.mockReset();
    getMonthlyHoursPdfPreviewMock.mockResolvedValue({
      pages: ['<svg>page 1</svg>', '<svg>page 2</svg>'],
    });

    let objectUrlIndex = 0;
    Object.defineProperty(window.URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => `blob:timer-preview-${++objectUrlIndex}`),
    });
    Object.defineProperty(window.URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
  });

  it('renders server-generated preview pages inside Workslip without a native PDF iframe', async () => {
    render(<AdminHoursExport data={data} monthLabel="august 2026" />);

    fireEvent.click(screen.getByRole('button', { name: 'Vis PDF' }));

    const dialog = await screen.findByRole('dialog', { name: 'PDF-preview af timer for august 2026' });
    expect(dialog).toBeInTheDocument();
    expect(getMonthlyHoursPdfPreviewMock).toHaveBeenCalledWith(2026, 8);
    expect(screen.queryByTitle('PDF-preview af timer for august 2026')).not.toBeInTheDocument();

    const pages = screen.getAllByRole('img');
    expect(pages).toHaveLength(2);
    expect(pages[0]).toHaveAttribute('src', 'blob:timer-preview-1');
    expect(pages[0]).toHaveAttribute('alt', 'Side 1 af 2');
    expect(pages[1]).toHaveAttribute('src', 'blob:timer-preview-2');
  });

  it('revokes every SVG page URL when the preview closes', async () => {
    render(<AdminHoursExport data={data} monthLabel="august 2026" />);

    fireEvent.click(screen.getByRole('button', { name: 'Vis PDF' }));
    await screen.findByRole('dialog', { name: 'PDF-preview af timer for august 2026' });
    fireEvent.click(screen.getByRole('button', { name: 'Luk PDF-preview' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(window.URL.revokeObjectURL).toHaveBeenCalledWith('blob:timer-preview-1');
    expect(window.URL.revokeObjectURL).toHaveBeenCalledWith('blob:timer-preview-2');
  });
});
