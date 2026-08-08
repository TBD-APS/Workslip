export type MyWorksheetEntryResponse = {
  workDate: string;
  jobId: string;
  userId: string;
  reportNumber: string | null;
  customerName: string;
  customerAddress: string | null;
  hoursWorked: number | string;
  hasOutlay: boolean;
  userDisplayName?: string | null;
};

export type MyWorksheetDayResponse = {
  date: string;
  totalHours: number | string;
  outlayCount: number;
  entries: MyWorksheetEntryResponse[];
};

export type MyWorksheetWeekResponse = {
  weekStart: string;
  weekEnd: string;
  totalHours: number | string;
  outlayCount: number;
  days: MyWorksheetDayResponse[];
};

export type MyWorksheetsMonthResponse = {
  year: number;
  month: number;
  monthStart: string;
  monthEnd: string;
  totalHours: number | string;
  outlayCount: number;
  weeks: MyWorksheetWeekResponse[];
};
