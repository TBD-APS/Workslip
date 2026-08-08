import {
  createPdfFilePreview,
  downloadPdfFile,
  type PdfFilePreview,
} from '../../../lib/pdfFile';

type JobReportPdfTarget = {
  id: string;
  reportNumber: string | null;
};

export type JobReportPdfPreview = PdfFilePreview;

function requestFor(job: JobReportPdfTarget) {
  return {
    url: `/api/jobs/${job.id}/report/pdf`,
    fallbackFileName: `rapport-${(job.reportNumber || job.id.slice(0, 8)).toUpperCase()}.pdf`,
  };
}

export function createJobReportPdfPreview(job: JobReportPdfTarget): Promise<JobReportPdfPreview> {
  return createPdfFilePreview(requestFor(job));
}

export function downloadJobReportPdf(job: JobReportPdfTarget): Promise<void> {
  return downloadPdfFile(requestFor(job));
}
