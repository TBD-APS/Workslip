import { useEffect, useMemo, useRef, useState } from 'react';
import { Download, Eye, Loader2 } from 'lucide-react';
import { notify } from '../../../lib/toast';
import { downloadPdfFile, openPdfFilePreview } from '../../../lib/pdfFile';
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

const OBJECT_URL_LIFETIME_MS = 60_000;

export function AdminHoursExport({ data, monthLabel }: AdminHoursExportProps) {
  const rows = useMemo(() => buildHoursExportRows(data), [data]);
  const [pdfAction, setPdfAction] = useState<PdfAction | null>(null);
  const previewUrlRef = useRef<string | null>(null);
  const hasRows = rows.length > 0;
  const pdfRequest = useMemo(() => ({
    url: `/api/worksheets/all/report/pdf?year=${data.year}&month=${data.month}`,
    fallbackFileName: `workslip-timer-${data.year}-${String(data.month).padStart(2, '0')}.pdf`,
  }), [data.month, data.year]);

  useEffect(() => () => {
    if (previewUrlRef.current) {
      window.URL.revokeObjectURL(previewUrlRef.current);
    }
  }, []);

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
      const { url } = await openPdfFilePreview(pdfRequest);
      if (previewUrlRef.current) {
        window.URL.revokeObjectURL(previewUrlRef.current);
      }
      previewUrlRef.current = url;
      setTimeout(() => {
        if (previewUrlRef.current === url) {
          window.URL.revokeObjectURL(url);
          previewUrlRef.current = null;
        }
      }, OBJECT_URL_LIFETIME_MS);
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
          onClick={previewPdf}
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
          onClick={downloadPdf}
          disabled={!hasRows || pdfAction !== null}
        >
          {pdfAction === 'download'
            ? <Loader2 className="hours-export-spinner" size={17} aria-hidden="true" />
            : <Download size={17} aria-hidden="true" />}
          Download PDF
        </button>
      </div>
    </section>
  );
}
