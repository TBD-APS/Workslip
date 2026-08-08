// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { getMock } = vi.hoisted(() => ({ getMock: vi.fn() }));

vi.mock('../api/fetcherOrval', () => ({
  AXIOS_INSTANCE: { get: getMock },
}));

import { createPdfFilePreview, downloadPdfFile, openPdfFilePreview } from './pdfFile';

describe('pdf file helper', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
    getMock.mockReset();
    Object.defineProperty(window.URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:pdf-preview'),
    });
    Object.defineProperty(window.URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
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

  it('opens the preview window synchronously before the authenticated fetch resolves', async () => {
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    let resolveRequest!: (value: { data: Blob; headers: Record<string, string> }) => void;
    getMock.mockImplementation(() => new Promise((resolve) => {
      resolveRequest = resolve;
    }));

    const replace = vi.fn();
    const close = vi.fn();
    const previewWindow = {
      closed: false,
      opener: window,
      location: { replace },
      close,
    } as unknown as Window;
    const open = vi.spyOn(window, 'open').mockReturnValue(previewWindow);

    const previewPromise = openPdfFilePreview({
      url: '/api/worksheets/all/report/pdf?year=2026&month=8',
      fallbackFileName: 'fallback.pdf',
    });

    expect(open).toHaveBeenCalledWith('', '_blank');
    expect(getMock).toHaveBeenCalledTimes(1);
    expect(replace).not.toHaveBeenCalled();

    resolveRequest({ data: blob, headers: { 'content-type': 'application/pdf' } });
    await expect(previewPromise).resolves.toMatchObject({ url: 'blob:pdf-preview' });

    expect(replace).toHaveBeenCalledWith('blob:pdf-preview');
    expect(close).not.toHaveBeenCalled();
  });

  it('closes the placeholder window when the PDF request fails', async () => {
    getMock.mockRejectedValue(new Error('network failure'));
    const close = vi.fn();
    const previewWindow = {
      closed: false,
      opener: window,
      location: { replace: vi.fn() },
      close,
    } as unknown as Window;
    vi.spyOn(window, 'open').mockReturnValue(previewWindow);

    await expect(openPdfFilePreview({
      url: '/api/example.pdf',
      fallbackFileName: 'fallback.pdf',
    })).rejects.toThrow('network failure');

    expect(close).toHaveBeenCalledTimes(1);
  });

  it('keeps download Blob URLs alive long enough for slower mobile browsers', async () => {
    vi.useFakeTimers();
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    getMock.mockResolvedValue({ data: blob, headers: {} });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const revokeObjectURL = vi.mocked(window.URL.revokeObjectURL);

    await downloadPdfFile({
      url: '/api/example.pdf',
      fallbackFileName: 'fallback.pdf',
    });

    expect(click).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    vi.advanceTimersByTime(59_999);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:pdf-preview');
  });
});
