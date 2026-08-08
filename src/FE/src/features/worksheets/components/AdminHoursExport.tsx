import { useMemo } from 'react';
import { Download, Printer } from 'lucide-react';
import type { MyWorksheetsMonthResponse } from '../worksheetOverviewTypes';
import {
  buildEmployeeHoursSummaries,
  buildHoursCsv,
  buildHoursExportRows,
  getExportWeekNumbers,
  hoursExportFilename,
  sumExportHours,
} from '../utils/hoursExport';
import './AdminHoursExport.css';

const PRINT_DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

const ROW_DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
});

type AdminHoursExportProps = {
  data: MyWorksheetsMonthResponse;
  monthLabel: string;
};

export function AdminHoursExport({ data, monthLabel }: AdminHoursExportProps) {
  const rows = useMemo(() => buildHoursExportRows(data), [data]);
  const employees = useMemo(() => buildEmployeeHoursSummaries(rows), [rows]);
  const weekNumbers = useMemo(() => getExportWeekNumbers(rows), [rows]);
  const totalHours = useMemo(() => sumExportHours(rows), [rows]);
  const weekTotals = useMemo(() => new Map(
    weekNumbers.map((week) => [
      week,
      rows.filter((row) => row.week === week).reduce((sum, row) => sum + row.hours, 0),
    ]),
  ), [rows, weekNumbers]);

  const hasRows = rows.length > 0;

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
    window.setTimeout(() => URL.revokeObjectURL(url), 0);
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
            disabled={!hasRows}
          >
            <Download size={17} aria-hidden="true" />
            CSV til Excel
          </button>
          <button
            type="button"
            className="btn btn-primary hours-export-button"
            onClick={() => window.print()}
            disabled={!hasRows}
          >
            <Printer size={17} aria-hidden="true" />
            Print / PDF
          </button>
        </div>
      </section>

      {hasRows && (
        <section className="hours-print-report" aria-hidden="true">
          <header className="hours-print-header">
            <div>
              <div className="hours-print-brand">WORKSLIP</div>
              <h1>Timeoversigt · {monthLabel}</h1>
              <p>{formatDateRange(data.monthStart, data.monthEnd)}</p>
            </div>
            <div className="hours-print-generated">
              Udskrevet {PRINT_DATE_FORMATTER.format(new Date())}
            </div>
          </header>

          <div className="hours-print-kpis">
            <PrintKpi label="Timer i alt" value={`${formatHours(totalHours)} t`} />
            <PrintKpi label="Medarbejdere" value={String(employees.length)} />
            <PrintKpi label="Registreringer" value={String(rows.length)} />
          </div>

          <section className="hours-print-section">
            <h2>Overblik pr. medarbejder</h2>
            <table className="hours-print-table hours-print-summary-table">
              <thead>
                <tr>
                  <th>Medarbejder</th>
                  {weekNumbers.map((week) => <th key={week}>Uge {week}</th>)}
                  <th>I alt</th>
                </tr>
              </thead>
              <tbody>
                {employees.map((employee) => (
                  <tr key={employee.userId}>
                    <td>{employee.employeeName}</td>
                    {weekNumbers.map((week) => (
                      <td key={week}>{formatHours(employee.weeklyHours.get(week) ?? 0)}</td>
                    ))}
                    <td><strong>{formatHours(employee.totalHours)}</strong></td>
                  </tr>
                ))}
                <tr className="hours-print-total-row">
                  <td>I alt</td>
                  {weekNumbers.map((week) => (
                    <td key={week}>{formatHours(weekTotals.get(week) ?? 0)}</td>
                  ))}
                  <td>{formatHours(totalHours)}</td>
                </tr>
              </tbody>
            </table>
          </section>

          <section className="hours-print-section hours-print-detail-section">
            <h2>Detaljer</h2>
            {employees.map((employee) => (
              <article key={employee.userId} className="hours-print-employee-block">
                <div className="hours-print-employee-heading">
                  <h3>{employee.employeeName}</h3>
                  <strong>{formatHours(employee.totalHours)} timer</strong>
                </div>
                <table className="hours-print-table hours-print-detail-table">
                  <thead>
                    <tr>
                      <th>Dato</th>
                      <th>Uge</th>
                      <th>Sag</th>
                      <th>Kunde</th>
                      <th>Timer</th>
                    </tr>
                  </thead>
                  <tbody>
                    {employee.rows.map((row, index) => (
                      <tr key={`${row.workDate}-${row.reportNumber}-${row.customerName}-${index}`}>
                        <td>{formatWorkDate(row.workDate)}</td>
                        <td>{row.week}</td>
                        <td>{row.reportNumber}</td>
                        <td>{row.customerName}</td>
                        <td>{formatHours(row.hours)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </article>
            ))}
          </section>
        </section>
      )}
    </>
  );
}

function PrintKpi({ label, value }: { label: string; value: string }) {
  return (
    <div className="hours-print-kpi">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function formatHours(value: number): string {
  return new Intl.NumberFormat('da-DK', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(value);
}

function formatDateRange(start: string, end: string): string {
  return `${formatWorkDate(start)} – ${formatWorkDate(end)}`;
}

function formatWorkDate(value: string): string {
  return ROW_DATE_FORMATTER.format(new Date(`${value}T00:00:00`));
}
