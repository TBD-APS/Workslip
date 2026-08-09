import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { MyWorksheetsMonthResponse } from '../worksheetOverviewTypes';

const { createPdfFilePreviewMock, downloadPdfFileMock } = vi.hoisted(() => ({
  createPdfFilePreviewMock: vi.fn(),
  downloadPdfFileMock: vi.fn(),
}));

vi.mock('../../../lib/pdfFile', () => ({
  createPdfFilePreview: createPdfFilePreviewMock,
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
    createPdfFilePreviewMock.mockReset();
    downloadPdfFileMock.mockReset();
    createPdfFilePreviewMock.mockResolvedValue({
      blob: new Blob(['pdf'], { type: 'application/pdf' }),
      fileName: 'workslip-timer-2026-08.pdf',
      url: 'blob:timer-preview',
    });
    Object.defineProperty(window.URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
  });

  it('renders the authenticated PDF Blob inside Workslip without opening a popup', async () => {
    const open = vi.spyOn(window, 'open');
    render(<AdminHoursExport data={data} monthLabel="august 2026" />);

    fireEvent.click(screen.getByRole('button', { name: 'Vis PDF' }));

    const dialog = await screen.findByRole('dialog', { name: 'PDF-preview af timer for august 2026' });
    expect(dialog).toBeInTheDocument();
    expect(createPdfFilePreviewMock).toHaveBeenCalledWith({
      url: '/api/worksheets/all/report/pdf?year=2026&month=8',
      fallbackFileName: 'workslip-timer-2026-08.pdf',
    });
    expect(open).not.toHaveBeenCalled();

    const frame = screen.getByTitle('PDF-preview af timer for august 2026');
    expect(frame).toHaveAttribute('src', 'blob:timer-preview');
  });

  it('revokes the preview Blob URL when the preview closes', async () => {
    render(<AdminHoursExport data={data} monthLabel="august 2026" />);

    fireEvent.click(screen.getByRole('button', { name: 'Vis PDF' }));
    await screen.findByRole('dialog', { name: 'PDF-preview af timer for august 2026' });
    fireEvent.click(screen.getByRole('button', { name: 'Luk PDF-preview' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(window.URL.revokeObjectURL).toHaveBeenCalledWith('blob:timer-preview');
  });
});
