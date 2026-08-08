import type { MyWorksheetsMonthResponse } from '../worksheetOverviewTypes';

export type HoursExportRow = {
  workDate: string;
  week: number;
  userId: string;
  employeeName: string;
  reportNumber: string;
  customerName: string;
  hours: number;
};

export type EmployeeHoursSummary = {
  userId: string;
  employeeName: string;
  totalHours: number;
  weeklyHours: Map<number, number>;
  rows: HoursExportRow[];
};

const DANISH_NAME_COLLATOR = new Intl.Collator('da-DK', { sensitivity: 'base' });

export function buildHoursExportRows(data: MyWorksheetsMonthResponse): HoursExportRow[] {
  const rows = data.weeks.flatMap((week) =>
    week.days.flatMap((day) =>
      day.entries
        .filter((entry) => entry.workDate >= data.monthStart && entry.workDate <= data.monthEnd)
        .map((entry) => ({
          workDate: entry.workDate,
          week: getIsoWeek(entry.workDate),
          userId: entry.userId,
          employeeName: entry.userDisplayName?.trim() || 'Ukendt medarbejder',
          reportNumber: entry.reportNumber?.trim() || '—',
          customerName: entry.customerName.trim() || 'Ukendt kunde',
          hours: Number(entry.hoursWorked),
        })),
    ),
  );

  return rows.sort((left, right) => {
    const employeeComparison = DANISH_NAME_COLLATOR.compare(left.employeeName, right.employeeName);
    if (employeeComparison !== 0) return employeeComparison;

    const userComparison = left.userId.localeCompare(right.userId);
    if (userComparison !== 0) return userComparison;

    const dateComparison = left.workDate.localeCompare(right.workDate);
    if (dateComparison !== 0) return dateComparison;

    const reportComparison = left.reportNumber.localeCompare(right.reportNumber, 'da-DK');
    if (reportComparison !== 0) return reportComparison;

    return DANISH_NAME_COLLATOR.compare(left.customerName, right.customerName);
  });
}

export function buildEmployeeHoursSummaries(rows: HoursExportRow[]): EmployeeHoursSummary[] {
  const byUser = new Map<string, EmployeeHoursSummary>();

  for (const row of rows) {
    const existing = byUser.get(row.userId);
    const summary = existing ?? {
      userId: row.userId,
      employeeName: row.employeeName,
      totalHours: 0,
      weeklyHours: new Map<number, number>(),
      rows: [],
    };

    summary.totalHours += row.hours;
    summary.weeklyHours.set(row.week, (summary.weeklyHours.get(row.week) ?? 0) + row.hours);
    summary.rows.push(row);

    if (!existing) byUser.set(row.userId, summary);
  }

  return Array.from(byUser.values()).sort((left, right) => {
    const employeeComparison = DANISH_NAME_COLLATOR.compare(left.employeeName, right.employeeName);
    return employeeComparison !== 0 ? employeeComparison : left.userId.localeCompare(right.userId);
  });
}

export function getExportWeekNumbers(rows: HoursExportRow[]): number[] {
  return Array.from(new Set(rows.map((row) => row.week))).sort((left, right) => left - right);
}

export function buildHoursCsv(rows: HoursExportRow[]): string {
  const header = ['Dato', 'Uge', 'Medarbejder', 'Sag', 'Kunde', 'Timer'].join(';');
  const lines = rows.map((row) => [
    csvText(row.workDate),
    String(row.week),
    csvText(row.employeeName),
    csvText(row.reportNumber),
    csvText(row.customerName),
    formatCsvHours(row.hours),
  ].join(';'));

  return `\uFEFF${[header, ...lines].join('\r\n')}`;
}

export function hoursExportFilename(data: MyWorksheetsMonthResponse): string {
  return `workslip-timer-${data.year}-${String(data.month).padStart(2, '0')}.csv`;
}

export function sumExportHours(rows: HoursExportRow[]): number {
  return rows.reduce((sum, row) => sum + row.hours, 0);
}

function csvText(value: string): string {
  return `"${value.replaceAll('"', '""')}"`;
}

function formatCsvHours(value: number): string {
  return new Intl.NumberFormat('da-DK', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
    useGrouping: false,
  }).format(value);
}

function getIsoWeek(value: string): number {
  const date = new Date(`${value}T00:00:00`);
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() + 3 - ((date.getDay() + 6) % 7));
  const week1 = new Date(date.getFullYear(), 0, 4);
  return 1 + Math.round(((date.getTime() - week1.getTime()) / 86400000 - 3 + ((week1.getDay() + 6) % 7)) / 7);
}
