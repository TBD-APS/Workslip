import { AXIOS_INSTANCE } from '../../../api/fetcherOrval';

type JobReportPdfTarget = {
  id: string;
  reportNumber: string | null;
};

export type JobReportPdfPreview = {
  url: string;
  fileName: string;
  blob: Blob;
};

async function fetchJobReportPdf(job: JobReportPdfTarget) {
  const response = await AXIOS_INSTANCE.get<Blob>(`/api/jobs/${job.id}/report/pdf`, {
    responseType: 'blob',
    headers: { Accept: 'application/pdf' },
  });
  const contentType = getHeaderValue(response.headers['content-type']) ?? 'application/pdf';
  const blob = response.data.type ? response.data : new Blob([response.data], { type: contentType });
  const fileName = getPdfFileName(response.headers['content-disposition'], job);

  return { blob, fileName };
}

export async function createJobReportPdfPreview(job: JobReportPdfTarget): Promise<JobReportPdfPreview> {
  const { blob, fileName } = await fetchJobReportPdf(job);
  return {
    blob,
    fileName,
    url: window.URL.createObjectURL(blob),
  };
}

export async function downloadJobReportPdf(job: JobReportPdfTarget): Promise<void> {
  const { blob, fileName } = await fetchJobReportPdf(job);
  triggerBrowserDownload(blob, fileName);
}

export function triggerBrowserDownload(blob: Blob, fileName: string) {
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

function getPdfFileName(contentDisposition: unknown, job: JobReportPdfTarget) {
  const header = getHeaderValue(contentDisposition);
  const match = header?.match(/filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i);
  const fileName = match?.[1] ?? match?.[2];

  if (fileName) return decodeURIComponent(fileName);

  return `rapport-${(job.reportNumber || job.id.slice(0, 8)).toUpperCase()}.pdf`;
}

function getHeaderValue(value: unknown) {
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) return value[0];
  return undefined;
}
