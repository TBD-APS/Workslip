import { AXIOS_INSTANCE } from '../api/fetcherOrval';

export type PdfFilePreview = {
  url: string;
  fileName: string;
  blob: Blob;
};

type PdfFileRequest = {
  url: string;
  fallbackFileName: string;
};

async function fetchPdfFile(request: PdfFileRequest) {
  const response = await AXIOS_INSTANCE.get<Blob>(request.url, {
    responseType: 'blob',
    headers: { Accept: 'application/pdf' },
  });
  const contentType = getHeaderValue(response.headers['content-type']) ?? 'application/pdf';
  const blob = response.data.type ? response.data : new Blob([response.data], { type: contentType });
  const fileName = getPdfFileName(response.headers['content-disposition'], request.fallbackFileName);

  return { blob, fileName };
}

export async function createPdfFilePreview(request: PdfFileRequest): Promise<PdfFilePreview> {
  const { blob, fileName } = await fetchPdfFile(request);
  return {
    blob,
    fileName,
    url: window.URL.createObjectURL(blob),
  };
}

export async function downloadPdfFile(request: PdfFileRequest): Promise<void> {
  const { blob, fileName } = await fetchPdfFile(request);
  triggerBrowserDownload(blob, fileName);
}

function triggerBrowserDownload(blob: Blob, fileName: string) {
  const url = window.URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  setTimeout(() => window.URL.revokeObjectURL(url), 1000);
}

function getPdfFileName(contentDisposition: unknown, fallbackFileName: string) {
  const header = getHeaderValue(contentDisposition);
  const match = header?.match(/filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i);
  const fileName = match?.[1] ?? match?.[2];

  if (fileName) return decodeURIComponent(fileName);

  return fallbackFileName;
}

function getHeaderValue(value: unknown) {
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) return value[0];
  return undefined;
}
