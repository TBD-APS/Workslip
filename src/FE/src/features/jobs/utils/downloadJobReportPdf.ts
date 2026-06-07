import { AXIOS_INSTANCE } from '../../../api/fetcherOrval';

type JobReportPdfTarget = {
  id: string;
  reportNumber: string | null;
};

export type JobReportPdfPreview = {
  url: string;
  fileName: string;
};

export async function createJobReportPdfPreview(job: JobReportPdfTarget): Promise<JobReportPdfPreview> {
  const response = await AXIOS_INSTANCE.get<Blob>(`/api/jobs/${job.id}/report/pdf`, {
    responseType: 'blob',
    headers: { Accept: 'application/pdf' },
  });
  const contentType = getHeaderValue(response.headers['content-type']) ?? 'application/pdf';
  const blob = response.data.type ? response.data : new Blob([response.data], { type: contentType });

  return {
    url: window.URL.createObjectURL(blob),
    fileName: getPdfFileName(response.headers['content-disposition'], job),
  };
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
