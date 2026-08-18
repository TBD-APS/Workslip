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
    window.localStorage.clear();
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
    window.localStorage.clear();
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

  it('reuses a freshly previewed PDF for the matching download version', async () => {
    vi.useFakeTimers();
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    getMock.mockResolvedValue({ data: blob, headers: {} });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const request = {
      url: '/api/jobs/job-1/report/pdf',
      fallbackFileName: 'rapport-1.pdf',
      reuseKey: 'job-1:version-1',
    };

    window.localStorage.setItem('authToken', 'token-a');
    await createPdfFilePreview(request);
    await downloadPdfFile(request);

    expect(getMock).toHaveBeenCalledTimes(1);
    expect(click).toHaveBeenCalledTimes(1);
  });

  it('refetches the PDF when the requested report version changed after preview', async () => {
    vi.useFakeTimers();
    const firstBlob = new Blob(['first'], { type: 'application/pdf' });
    const updatedBlob = new Blob(['updated'], { type: 'application/pdf' });
    getMock
      .mockResolvedValueOnce({ data: firstBlob, headers: {} })
      .mockResolvedValueOnce({ data: updatedBlob, headers: {} });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    window.localStorage.setItem('authToken', 'token-a');
    await createPdfFilePreview({
      url: '/api/jobs/job-1/report/pdf',
      fallbackFileName: 'rapport-1.pdf',
      reuseKey: 'job-1:version-1',
    });
    await downloadPdfFile({
      url: '/api/jobs/job-1/report/pdf',
      fallbackFileName: 'rapport-1.pdf',
      reuseKey: 'job-1:version-2',
    });

    expect(getMock).toHaveBeenCalledTimes(2);
  });

  it('refetches the PDF when the authenticated session changed after preview', async () => {
    vi.useFakeTimers();
    const firstBlob = new Blob(['first'], { type: 'application/pdf' });
    const secondBlob = new Blob(['second'], { type: 'application/pdf' });
    getMock
      .mockResolvedValueOnce({ data: firstBlob, headers: {} })
      .mockResolvedValueOnce({ data: secondBlob, headers: {} });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const request = {
      url: '/api/jobs/job-1/report/pdf',
      fallbackFileName: 'rapport-1.pdf',
      reuseKey: 'job-1:version-1',
    };

    window.localStorage.setItem('authToken', 'token-a');
    await createPdfFilePreview(request);
    window.localStorage.setItem('authToken', 'token-b');
    await downloadPdfFile(request);

    expect(getMock).toHaveBeenCalledTimes(2);
  });

  it('does not cache a PDF response when the authenticated session changes while preview is loading', async () => {
    vi.useFakeTimers();
    const firstBlob = new Blob(['first'], { type: 'application/pdf' });
    const secondBlob = new Blob(['second'], { type: 'application/pdf' });
    let resolvePreview!: (value: { data: Blob; headers: Record<string, string> }) => void;
    getMock
      .mockImplementationOnce(() => new Promise((resolve) => {
        resolvePreview = resolve;
      }))
      .mockResolvedValueOnce({ data: secondBlob, headers: {} });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const request = {
      url: '/api/jobs/job-1/report/pdf',
      fallbackFileName: 'rapport-1.pdf',
      reuseKey: 'job-1:version-1',
    };

    window.localStorage.setItem('authToken', 'token-a');
    const previewPromise = createPdfFilePreview(request);
    expect(getMock).toHaveBeenCalledTimes(1);

    window.localStorage.setItem('authToken', 'token-b');
    resolvePreview({ data: firstBlob, headers: {} });
    await previewPromise;
    await downloadPdfFile(request);

    expect(getMock).toHaveBeenCalledTimes(2);
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
