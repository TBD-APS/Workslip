// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { getMock } = vi.hoisted(() => ({ getMock: vi.fn() }));

vi.mock('../api/fetcherOrval', () => ({
  AXIOS_INSTANCE: { get: getMock },
}));

import { createPdfFilePreview } from './pdfFile';

describe('pdf file helper', () => {
  beforeEach(() => {
    getMock.mockReset();
    Object.defineProperty(window.URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:pdf-preview'),
    });
  });

  it('uses the authenticated API client and server filename for previews', async () => {
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    getMock.mockResolvedValue({
      data: blob,
      headers: {
        'content-type': 'application/pdf',
        'content-disposition': 'attachment; filename="workslip-timer-2026-08.pdf"',
      },
    });

    const preview = await createPdfFilePreview({
      url: '/api/worksheets/all/report/pdf?year=2026&month=8',
      fallbackFileName: 'fallback.pdf',
    });

    expect(getMock).toHaveBeenCalledWith('/api/worksheets/all/report/pdf?year=2026&month=8', {
      responseType: 'blob',
      headers: { Accept: 'application/pdf' },
    });
    expect(preview).toEqual({ blob, fileName: 'workslip-timer-2026-08.pdf', url: 'blob:pdf-preview' });
  });

  it('falls back to the caller filename when content-disposition is absent', async () => {
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    getMock.mockResolvedValue({ data: blob, headers: {} });

    const preview = await createPdfFilePreview({
      url: '/api/example.pdf',
      fallbackFileName: 'fallback.pdf',
    });

    expect(preview.fileName).toBe('fallback.pdf');
  });
});
