import { describe, expect, it } from 'vitest';
import type { MyWorksheetsMonthResponse } from '../worksheetOverviewTypes';
import {
  buildEmployeeHoursSummaries,
  buildHoursCsv,
  buildHoursExportRows,
  hoursExportFilename,
  sumExportHours,
  type HoursExportRow,
} from './hoursExport';

const monthData: MyWorksheetsMonthResponse = {
  year: 2026,
  month: 8,
  monthStart: '2026-08-01',
  monthEnd: '2026-08-31',
  totalHours: 10,
  outlayCount: 0,
  weeks: [
    {
      weekStart: '2026-07-27',
      weekEnd: '2026-08-02',
      totalHours: 2.5,
      outlayCount: 0,
      days: [
        {
          date: '2026-07-31',
          totalHours: 9,
          outlayCount: 0,
          entries: [
            {
              workDate: '2026-07-31',
              jobId: 'job-outside',
              userId: 'user-1',
              reportNumber: '99',
              customerName: 'Skal ikke med',
              customerAddress: 'Privatvej 1',
              hoursWorked: 9,
              hasOutlay: false,
              userDisplayName: 'Alex Jensen',
            },
          ],
        },
        {
          date: '2026-08-01',
          totalHours: 2.5,
          outlayCount: 0,
          entries: [
            {
              workDate: '2026-08-01',
              jobId: 'job-1',
              userId: 'user-1',
              reportNumber: '101',
              customerName: 'ACME; "Nord"',
              customerAddress: 'Kystvej 22',
              hoursWorked: 2.5,
              hasOutlay: false,
              userDisplayName: 'Alex Jensen',
            },
          ],
        },
      ],
    },
    {
      weekStart: '2026-08-03',
      weekEnd: '2026-08-09',
      totalHours: 7.5,
      outlayCount: 0,
      days: [
        {
          date: '2026-08-03',
          totalHours: 7.5,
          outlayCount: 0,
          entries: [
            {
              workDate: '2026-08-03',
              jobId: 'job-2',
              userId: 'user-2',
              reportNumber: '102',
              customerName: 'Beta VVS',
              customerAddress: null,
              hoursWorked: '7.5',
              hasOutlay: false,
              userDisplayName: 'Alex Jensen',
            },
          ],
        },
      ],
    },
  ],
};

describe('hours export', () => {
  it('exports only entries inside the selected month and keeps same-name users separate', () => {
    const rows = buildHoursExportRows(monthData);
    const employees = buildEmployeeHoursSummaries(rows);

    expect(rows).toHaveLength(2);
    expect(rows.map((row) => row.workDate)).toEqual(['2026-08-01', '2026-08-03']);
    expect(sumExportHours(rows)).toBe(10);

    expect(employees).toHaveLength(2);
    expect(employees.map((employee) => employee.userId)).toEqual(['user-1', 'user-2']);
    expect(employees.map((employee) => employee.totalHours)).toEqual([2.5, 7.5]);
  });

  it('keeps legacy worksheet responses without userId renderable during deploy skew', () => {
    const legacyData = structuredClone(monthData);
    for (const week of legacyData.weeks) {
      for (const day of week.days) {
        for (const entry of day.entries) {
          delete (entry as { userId?: string }).userId;
        }
      }
    }

    const rows = buildHoursExportRows(legacyData);
    const employees = buildEmployeeHoursSummaries(rows);

    expect(rows).toHaveLength(2);
    expect(rows.every((row) => row.userId === 'legacy:alex jensen')).toBe(true);
    expect(sumExportHours(rows)).toBe(10);
    expect(employees).toHaveLength(1);
    expect(employees[0]?.totalHours).toBe(10);
  });

  it('creates a Danish Excel-friendly, privacy-minimized CSV', () => {
    const rows = buildHoursExportRows(monthData);
    const csv = buildHoursCsv(rows);

    expect(csv.startsWith('\uFEFFDato;Uge;Medarbejder;Sag;Kunde;Timer\r\n')).toBe(true);
    expect(csv).toContain('"ACME; ""Nord""";2,5');
    expect(csv).toContain('"Beta VVS";7,5');
    expect(csv).not.toContain('Kystvej 22');
    expect(csv).not.toContain('user-1');
    expect(csv).not.toContain('job-1');
    expect(csv).not.toContain('Skal ikke med');
  });

  it('neutralizes spreadsheet formula prefixes in exported text', () => {
    const row: HoursExportRow = {
      workDate: '2026-08-04',
      week: 32,
      userId: 'internal-user-id',
      employeeName: '+Alex Jensen',
      reportNumber: '=1+1',
      customerName: '@SUM(A1:A2)',
      hours: 1.5,
    };

    const csv = buildHoursCsv([row]);

    expect(csv).toContain("\"'+Alex Jensen\"");
    expect(csv).toContain("\"'=1+1\"");
    expect(csv).toContain("\"'@SUM(A1:A2)\"");
    expect(csv).not.toContain('internal-user-id');
  });

  it('uses a predictable month-based filename', () => {
    expect(hoursExportFilename(monthData)).toBe('workslip-timer-2026-08.csv');
  });
});