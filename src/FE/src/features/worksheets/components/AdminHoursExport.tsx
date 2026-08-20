import { useCallback, useMemo, useRef, useState } from 'react';
import { Download, Eye, Loader2, X } from 'lucide-react';
import { useModalAccessibility } from '../../../components/common/useModalAccessibility';
import { notify } from '../../../lib/toast';
import { downloadPdfFile } from '../../../lib/pdfFile';
import { getMonthlyHoursPdfPreview } from '../api/monthlyHoursPdfPreview';
import type { MyWorksheetsMonthResponse } from '../worksheetOverviewTypes';
import {
  buildHoursCsv,
  buildHoursExportRows,
  hoursExportFilename,
} from '../utils/hoursExport';
import './AdminHoursExport.css';

type AdminHoursExportProps = {
  data: MyWorksheetsMonthResponse;
  monthLabel: string;
};

type PdfAction = 'preview' | 'download';

type PdfPreview = {
  fileName: string;
  pageUrls: string[];
};

const OBJECT_URL_LIFETIME_MS = 60_000;

export function AdminHoursExport({ data, monthLabel }: AdminHoursExportProps) {
  const rows = useMemo(() => buildHoursExportRows(data), [data]);
  const [pdfAction, setPdfAction] = useState<PdfAction | null>(null);
  const [pdfPreview, setPdfPreview] = useState<PdfPreview | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const hasRows = rows.length > 0;
  const pdfRequest = useMemo(() => ({
    url: `/api/worksheets/all/report/pdf?year=${data.year}&month=${data.month}`,
    fallbackFileName: `workslip-timer-${data.year}-${String(data.month).padStart(2, '0')}.pdf`,
  }), [data.month, data.year]);

  const closePdfPreview = useCallback(() => {
    setPdfPreview(null);
  }, []);
  const previewDialogRef = useModalAccessibility<HTMLDivElement>({
    open: Boolean(pdfPreview),
    onClose: closePdfPreview,
    initialFocusRef: closeButtonRef,
  });

  const downloadCsv = () => {
    if (!hasRows) return;

    const blob = new Blob([buildHoursCsv(rows)], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = hoursExportFilename(data);
    link.hidden = true;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_LIFETIME_MS);
  };

  const previewPdf = async () => {
    if (!hasRows || pdfAction) return;
    setPdfAction('preview');

    try {
      const preview = await getMonthlyHoursPdfPreview(data.year, data.month);
      if (preview.pages.length === 0 || preview.contentType !== 'image/png') {
        throw new Error('worksheet_pdf_preview_invalid');
      }

      setPdfPreview({
        fileName: pdfRequest.fallbackFileName,
        pageUrls: preview.pages.map((page) => `data:${preview.contentType};base64,${page}`),
      });
    } catch {
      notify.error(`Kunne ikke hente PDF for ${monthLabel}`);
    } finally {
      setPdfAction(null);
    }
  };

  const downloadPdf = async () => {
    if (!hasRows || pdfAction) return;
    setPdfAction('download');

    try {
      await downloadPdfFile(pdfRequest);
    } catch {
      notify.error(`Kunne ikke hente PDF for ${monthLabel}`);
    } finally {
      setPdfAction(null);
    }
  };

  return (
    <>
      <section className="hours-export-toolbar" aria-label="Eksportér timer">
        <div className="hours-export-toolbar-copy">
          <strong>Eksportér {monthLabel}</strong>
          <span>{hasRows ? `${rows.length} registreringer klar` : 'Ingen registreringer at eksportere'}</span>
        </div>
        <div className="hours-export-actions">
          <button
            type="button"
            className="btn btn-secondary hours-export-button"
            onClick={downloadCsv}
            disabled={!hasRows || pdfAction !== null}
          >
            <Download size={17} aria-hidden="true" />
            CSV til Excel
          </button>
          <button
            type="button"
            className="btn btn-secondary hours-export-button"
            onClick={() => { void previewPdf(); }}
            disabled={!hasRows || pdfAction !== null}
          >
            {pdfAction === 'preview'
              ? <Loader2 className="hours-export-spinner" size={17} aria-hidden="true" />
              : <Eye size={17} aria-hidden="true" />}
            Vis PDF
          </button>
          <button
            type="button"
            className="btn btn-primary hours-export-button"
            onClick={() => { void downloadPdf(); }}
            disabled={!hasRows || pdfAction !== null}
          >
            {pdfAction === 'download'
              ? <Loader2 className="hours-export-spinner" size={17} aria-hidden="true" />
              : <Download size={17} aria-hidden="true" />}
            Download PDF
          </button>
        </div>
      </section>

      {pdfPreview && (
        <div
          ref={previewDialogRef}
          className="hours-pdf-preview-overlay"
          role="dialog"
          aria-modal="true"
          aria-label={`PDF-preview af timer for ${monthLabel}`}
          tabIndex={-1}
        >
          <header className="hours-pdf-preview-header">
            <div className="hours-pdf-preview-title">
              <strong>{monthLabel}</strong>
              <span>{pdfPreview.fileName}</span>
            </div>
            <button
              ref={closeButtonRef}
              type="button"
              className="btn btn-secondary hours-pdf-preview-close"
              onClick={closePdfPreview}
              aria-label="Luk PDF-preview"
            >
              <X size={18} aria-hidden="true" />
              Luk
            </button>
          </header>
          <div
            className="hours-pdf-preview-pages"
            role="document"
            aria-label={`Dokumentpreview af timer for ${monthLabel}`}
          >
            {pdfPreview.pageUrls.map((url, index) => (
              <img
                key={index}
                className="hours-pdf-preview-page"
                src={url}
                alt={`Side ${index + 1} af ${pdfPreview.pageUrls.length}`}
              />
            ))}
          </div>
        </div>
      )}
    </>
  );
}
