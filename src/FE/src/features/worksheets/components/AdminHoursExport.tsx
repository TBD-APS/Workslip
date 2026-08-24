import { useCallback, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Download, ExternalLink, Eye, Loader2, X } from 'lucide-react';
import { useModalAccessibility } from '../../../components/common/useModalAccessibility';
import { apiClient } from '../../../lib/axios';
import { notify } from '../../../lib/toast';
import { downloadPdfFile } from '../../../lib/pdfFile';
import { useAuth } from '../../../providers/useAuth';
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

type PowerBiReportLinkResponse = {
  url: string | null;
  embedUrl: string | null;
};

const OBJECT_URL_LIFETIME_MS = 60_000;

export function AdminHoursExport({ data, monthLabel }: AdminHoursExportProps) {
  const { user } = useAuth();
  const rows = useMemo(() => buildHoursExportRows(data), [data]);
  const [pdfAction, setPdfAction] = useState<PdfAction | null>(null);
  const [pdfPreview, setPdfPreview] = useState<PdfPreview | null>(null);
  const [loadedPowerBiUrl, setLoadedPowerBiUrl] = useState<string | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const hasRows = rows.length > 0;
  const pdfRequest = useMemo(() => ({
    url: `/api/worksheets/all/report/pdf?year=${data.year}&month=${data.month}`,
    fallbackFileName: `workslip-timer-${data.year}-${String(data.month).padStart(2, '0')}.pdf`,
  }), [data.month, data.year]);
  const powerBiReportQuery = useQuery({
    queryKey: ['worksheets', 'power-bi-report', user?.organizationId ?? 'unknown'],
    queryFn: async () => (await apiClient.get(
      '/api/worksheets/all/report/power-bi',
      { skipGlobalErrorToast: true },
    )) as PowerBiReportLinkResponse,
    retry: false,
    staleTime: 5 * 60_000,
  });
  const powerBiReport = powerBiReportQuery.data;
  const isPowerBiFrameLoaded = Boolean(
    powerBiReport?.embedUrl && loadedPowerBiUrl === powerBiReport.embedUrl,
  );
  const showPowerBiReport = powerBiReportQuery.isError || Boolean(powerBiReport?.url);

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
      {hasRows && (
        <section className="hours-export-toolbar" aria-label="Eksportér timer">
          <div className="hours-export-toolbar-copy">
            <strong>Eksportér {monthLabel}</strong>
            <span>{rows.length} registreringer klar</span>
          </div>
          <div className="hours-export-actions">
            <button
              id="hours-export-csv-button"
              type="button"
              className="btn btn-secondary hours-export-button hours-export-csv-button"
              onClick={downloadCsv}
              disabled={pdfAction !== null}
            >
              <Download size={17} aria-hidden="true" />
              CSV til Excel
            </button>
            <button
              id="hours-export-preview-button"
              type="button"
              className="btn btn-secondary hours-export-button"
              onClick={() => { void previewPdf(); }}
              disabled={pdfAction !== null}
            >
              {pdfAction === 'preview'
                ? <Loader2 className="hours-export-spinner" size={17} aria-hidden="true" />
                : <Eye size={17} aria-hidden="true" />}
              Vis PDF
            </button>
            <button
              id="hours-export-download-button"
              type="button"
              className="btn btn-primary hours-export-button"
              onClick={() => { void downloadPdf(); }}
              disabled={pdfAction !== null}
            >
              {pdfAction === 'download'
                ? <Loader2 className="hours-export-spinner" size={17} aria-hidden="true" />
                : <Download size={17} aria-hidden="true" />}
              Download PDF
            </button>
          </div>
        </section>
      )}

      {showPowerBiReport && (
        <section id="timer-power-bi-report" className="power-bi-report" aria-labelledby="power-bi-report-title">
          <header className="power-bi-report-header">
            <div className="power-bi-report-copy">
              <strong id="power-bi-report-title">Power BI-overblik</strong>
              <span>Interaktiv timerapport direkte i Workslip</span>
            </div>
            {powerBiReport?.url && (
              <a
                id="timer-power-bi-open"
                className="btn btn-secondary power-bi-report-open"
                href={powerBiReport.url}
                target="_blank"
                rel="noopener noreferrer"
              >
                <ExternalLink size={17} aria-hidden="true" />
                Åbn i Power BI
              </a>
            )}
          </header>

          {powerBiReportQuery.isError && (
            <div className="power-bi-report-state power-bi-report-state--error" role="alert">
              <div>
                <strong>Power BI kunne ikke indlæses</strong>
                <span>Workslip kunne ikke hente rapportkonfigurationen.</span>
              </div>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => { void powerBiReportQuery.refetch(); }}
              >
                Prøv igen
              </button>
            </div>
          )}

          {powerBiReport?.url && !powerBiReport.embedUrl && (
            <div className="power-bi-report-state power-bi-report-state--error" role="alert">
              <div>
                <strong>Rapporten kan ikke indlejres sikkert</strong>
                <span>Åbn rapporten i Power BI, og kontrollér rapportlinkets konfiguration.</span>
              </div>
            </div>
          )}

          {powerBiReport?.embedUrl && (
            <div className={`power-bi-report-frame-shell ${isPowerBiFrameLoaded ? 'is-loaded' : ''}`}>
              {!isPowerBiFrameLoaded && (
                <div className="power-bi-report-frame-loading" role="status">
                  <Loader2 className="hours-export-spinner" size={22} aria-hidden="true" />
                  <span>Indlæser Power BI-rapport…</span>
                </div>
              )}
              <iframe
                id="timer-power-bi-frame"
                key={powerBiReport.embedUrl}
                className="power-bi-report-frame"
                src={powerBiReport.embedUrl}
                title="Power BI timerapport"
                loading="lazy"
                referrerPolicy="strict-origin-when-cross-origin"
                allowFullScreen
                onLoad={() => setLoadedPowerBiUrl(powerBiReport.embedUrl)}
              />
            </div>
          )}

          {powerBiReport?.embedUrl && (
            <p className="power-bi-report-note">
              Rapporten vises fra Microsoft Power BI og følger Power BI-adgang, licens og eventuelle RLS-regler.
            </p>
          )}
        </section>
      )}

      {pdfPreview && (
        <div
          id="hours-pdf-preview-dialog"
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
              id="hours-pdf-preview-close"
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
                id={`hours-pdf-preview-page-${index}`}
                key={index}
                className="hours-pdf-preview-page"
                src={url}
                loading={index === 0 ? 'eager' : 'lazy'}
                decoding="async"
                alt={`Side ${index + 1} af ${pdfPreview.pageUrls.length}`}
              />
            ))}
          </div>
        </div>
      )}
    </>
  );
}
