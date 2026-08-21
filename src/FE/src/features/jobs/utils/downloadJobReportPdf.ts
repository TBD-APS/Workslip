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
  const url = `/api/jobs/${job.id}/report/pdf`;
  return {
    url,
    fallbackFileName: `rapport-${(job.reportNumber || job.id.slice(0, 8)).toUpperCase()}.pdf`,
    // The actual object contains the complete report view model. Binding reuse to
    // that serialized snapshot prevents a preview Blob from surviving a report edit.
    reuseKey: `${url}:${JSON.stringify(job)}`,
  };
}

export function createJobReportPdfPreview(job: JobReportPdfTarget): Promise<JobReportPdfPreview> {
  return createPdfFilePreview(requestFor(job));
}

export function downloadJobReportPdf(job: JobReportPdfTarget): Promise<void> {
  return downloadPdfFile(requestFor(job));
}
