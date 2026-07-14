import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { BriefcaseBusiness, ChevronDown, ChevronLeft, ChevronRight, MapPin, ReceiptText, Timer, Users } from 'lucide-react';
import { ErrorState } from '../../../components/ErrorState';
import { apiClient } from '../../../lib/axios';
import { useIsAdmin } from '../../../providers/permissions';

type MyWorksheetEntryResponse = {
  workDate: string;
  jobId: string;
  reportNumber: string | null;
  customerName: string;
  customerAddress: string | null;
  hoursWorked: number | string;
  hasOutlay: boolean;
  userDisplayName?: string | null;
};

type MyWorksheetDayResponse = {
  date: string;
  totalHours: number | string;
  outlayCount: number;
  entries: MyWorksheetEntryResponse[];
};

type MyWorksheetWeekResponse = {
  weekStart: string;
  weekEnd: string;
  totalHours: number | string;
  outlayCount: number;
  days: MyWorksheetDayResponse[];
};

type MyWorksheetsMonthResponse = {
  year: number;
  month: number;
  monthStart: string;
  monthEnd: string;
  totalHours: number | string;
  outlayCount: number;
  weeks: MyWorksheetWeekResponse[];
};

type MonthCursor = { year: number; month: number };
type TimerOverviewState = {
  cursor: MonthCursor;
  expandedWeeks: string[];
  scrollTop: number;
};

const TIMER_OVERVIEW_STATE_KEY = 'workslip.timerOverviewState';

const MONTH_FORMATTER = new Intl.DateTimeFormat('da-DK', { month: 'short', year: 'numeric' });
const DAY_FORMATTER = new Intl.DateTimeFormat('da-DK', { weekday: 'short', day: 'numeric' });
const WEEK_RANGE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short' });

function AdminWeeklyOverview({
  data,
  currentWeekStart,
}: {
  data: MyWorksheetsMonthResponse;
  currentWeekStart: string;
}) {
  return (
    <section className="admin-weekly-overview">
      {data.weeks.map((week) => {
        const isCurrentWeek = week.weekStart === currentWeekStart;
        // Group entries by user and day
        const userDayMap = new Map<string, Map<string, { hours: number; entries: MyWorksheetEntryResponse[] }>>();
        
        week.days.forEach((day) => {
          day.entries.forEach((entry) => {
            const user = entry.userDisplayName || 'Ukendt';
            if (!userDayMap.has(user)) {
              userDayMap.set(user, new Map());
            }
            const dayMap = userDayMap.get(user)!;
            const key = day.date;
            if (!dayMap.has(key)) {
              dayMap.set(key, { hours: 0, entries: [] });
            }
            const dayData = dayMap.get(key)!;
            dayData.hours += Number(entry.hoursWorked);
            dayData.entries.push(entry);
          });
        });

        // Calculate totals per user
        const userTotals = new Map<string, number>();
        userDayMap.forEach((dayMap, user) => {
          let total = 0;
          dayMap.forEach((dayData) => {
            total += dayData.hours;
          });
          userTotals.set(user, total);
        });

        return (
          <div key={week.weekStart} className={`admin-week-card ${isCurrentWeek ? 'is-current' : ''}`}>
            <table className="admin-week-table">
              <thead>
                <tr>
                  <th className="admin-col-name admin-row-label">
                    <span className="admin-week-info">{formatWeekRange(week.weekStart, week.weekEnd)}</span>
                    <span className="admin-week-number">Uge {getIsoWeek(week.weekStart)}</span>
                  </th>
                  {week.days.map((day) => (
                    <th key={day.date} className="admin-col-day">
                      <span className="admin-day-short">{DAY_FORMATTER.format(parseDate(day.date)).split(' ')[0]}</span>
                      <span className="admin-day-num">{parseDate(day.date).getDate()}</span>
                    </th>
                  ))}
                  <th className="admin-col-total">I alt</th>
                </tr>
              </thead>
              <tbody>
                {Array.from(userDayMap.entries()).map(([user, dayMap]) => (
                  <tr key={user} className="admin-user-row">
                    <td className="admin-col-name admin-user-name">{user}</td>
                    {week.days.map((day) => {
                      const dayData = dayMap.get(day.date);
                      return (
                        <td key={day.date} className="admin-col-day admin-col-hours">
                          {dayData ? formatNumber(dayData.hours) : '—'}
                        </td>
                      );
                    })}
                    <td className="admin-col-total admin-user-total">{formatNumber(userTotals.get(user) || 0)}</td>
                  </tr>
                ))}
                <tr className="admin-row-total">
                   <td className="admin-col-name">I alt pr. dag</td>
                  {week.days.map((day) => (
                    <td key={day.date} className="admin-col-day admin-col-hours">
                      {formatNumber(day.totalHours)}
                    </td>
                  ))}
                  <td className="admin-col-total">{formatNumber(week.totalHours)}</td>
                </tr>
              </tbody>
            </table>
          </div>
        );
      })}
    </section>
  );
}

export function MyWorksheets() {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const savedState = useRef(readTimerOverviewState());
  const hasRestoredScroll = useRef(false);
  const [cursor, setCursor] = useState<MonthCursor>(() => savedState.current?.cursor ?? getCurrentMonthCursor());
  const [expandedWeeks, setExpandedWeeks] = useState<Set<string>>(() => {
    const saved = savedState.current?.expandedWeeks;
    // If we have saved state, use it; otherwise only expand current week
    if (saved && saved.length > 0) {
      return new Set(saved);
    }
    // Initialize with current week only
    return new Set([getCurrentWeekStart()]);
  });

  const endpoint = isAdmin ? '/api/worksheets/all' : '/api/worksheets/my';

  const query = useQuery({
    queryKey: ['worksheets', isAdmin ? 'all' : 'my', cursor],
    queryFn: async () => (await apiClient.get(endpoint, { params: cursor })) as MyWorksheetsMonthResponse,
  });

  const data = query.data;
  const monthLabel = useMemo(() => formatMonth(cursor), [cursor]);
  const isCurrentMonth = sameMonth(cursor, getCurrentMonthCursor());

  useEffect(() => {
    writeTimerOverviewState(cursor, expandedWeeks, getAppScrollTop());
  }, [cursor, expandedWeeks]);

  useEffect(() => {
    if (!data || hasRestoredScroll.current) {
      return;
    }

    hasRestoredScroll.current = true;
    const scrollTop = savedState.current?.scrollTop ?? 0;
    requestAnimationFrame(() => setAppScrollTop(scrollTop));
  }, [data]);

  const selectMonth = (nextCursor: MonthCursor) => {
    setCursor(nextCursor);
    setExpandedWeeks(new Set([getCurrentWeekStart()]));
    writeTimerOverviewState(nextCursor, new Set([getCurrentWeekStart()]), 0);
    requestAnimationFrame(() => setAppScrollTop(0));
  };

  const toggleWeek = (weekStart: string) => {
    setExpandedWeeks((current) => {
      const next = new Set(current);
      if (next.has(weekStart)) {
        next.delete(weekStart);
      } else {
        next.add(weekStart);
      }

      return next;
    });
  };

  const openJob = (jobId: string) => {
    writeTimerOverviewState(cursor, expandedWeeks, getAppScrollTop());
    navigate(`/app/completed/${jobId}`, { state: { from: '/app/timer' } });
  };

  return (
    <div className={`page-container time-overview-page ${isAdmin ? 'time-overview-page--admin' : ''}`}>
      <div className="page-header time-overview-header">
        <div>
          <h2>{isAdmin ? 'Alles timer' : 'Mine timer'}</h2>
          <p className="subtitle">Ugentligt overblik over sager, timer og udlæg</p>
        </div>
        <div className="time-month-controls">
          <div className="time-month-switcher" aria-label="Vælg måned">
            <button type="button" className="btn-icon time-month-button" onClick={() => selectMonth(addMonths(cursor, -1))} aria-label="Forrige måned">
              <ChevronLeft size={20} />
            </button>
            <span>{monthLabel}</span>
            <button type="button" className="btn-icon time-month-button" onClick={() => selectMonth(addMonths(cursor, 1))} aria-label="Næste måned">
              <ChevronRight size={20} />
            </button>
          </div>
          <button
            type="button"
            className="time-today-button"
            onClick={() => selectMonth(getCurrentMonthCursor())}
            disabled={isCurrentMonth}
          >
            Til nuværende måned
          </button>
          {data && (
            <section className="time-summary-grid" aria-label="Månedsopsummering">
              <SummaryCard icon={<Timer size={12} />} label="Timer" value={`${formatNumber(data.totalHours)} t`} />
              <SummaryCard icon={<ReceiptText size={12} />} label="Udlæg" value={`${data.outlayCount}`} />
              <SummaryCard icon={<BriefcaseBusiness size={12} />} label="Jobs" value={`${countEntries(data.weeks)}`} />
              {isAdmin && <SummaryCard icon={<Users size={12} />} label="Medarbejdere" value={`${countUniqueEmployees(data.weeks)}`} />}
            </section>
          )}
        </div>
      </div>

      {query.isLoading && (
        <div className="time-week-list">
          <div className="time-week-card time-week-skeleton" />
          <div className="time-week-card time-week-skeleton" />
        </div>
      )}

      {query.isError && (
        <ErrorState message={isAdmin ? 'Kunne ikke hente timer.' : 'Kunne ikke hente dine timer.'} onRetry={() => query.refetch()} />
      )}

      {data && (
        <>
          {countEntries(data.weeks) === 0 && (
            <div className="empty-state">
              <p>{isAdmin ? `Ingen timer registreret for nogen medarbejdere i ${monthLabel}.` : `Ingen timer registreret i ${monthLabel}.`}</p>
            </div>
          )}
          {isAdmin && data.weeks.length > 0 ? (
            <AdminWeeklyOverview data={data} currentWeekStart={getCurrentWeekStart()} />
          ) : (
            <section className="time-week-list" aria-label="Ugentligt timeoverblik">
              {data.weeks.map((week) => (
                <WeekCard
                  key={week.weekStart}
                  week={week}
                  month={data.month}
                  isExpanded={expandedWeeks.has(week.weekStart)}
                  onToggle={() => toggleWeek(week.weekStart)}
                  onOpenJob={openJob}
                />
              ))}
            </section>
          )}
        </>
      )}
    </div>
  );
}

function WeekCard({
  week,
  month,
  isExpanded,
  onToggle,
  onOpenJob,
}: {
  week: MyWorksheetWeekResponse;
  month: number;
  isExpanded: boolean;
  onToggle: () => void;
  onOpenJob: (jobId: string) => void;
}) {
  const entryCount = countWeekEntries(week);
  const contentId = `week-${week.weekStart}`;

  return (
    <article className={`time-week-card ${isExpanded ? 'is-expanded' : ''}`}>
      <div className="time-week-header">
        <div>
          <span className="job-number">{formatWeekRange(week.weekStart, week.weekEnd)} | Uge {getIsoWeek(week.weekStart)} </span>
        </div>
        <div className="time-week-totals">
          <span><Timer size={14} /> {formatNumber(week.totalHours)} t</span>
          <span><ReceiptText size={14} /> {week.outlayCount}</span>
          <span><BriefcaseBusiness size={14} /> {entryCount}</span>
          <button
            type="button"
            className="time-week-toggle"
            onClick={onToggle}
            aria-expanded={isExpanded}
            aria-controls={contentId}
          >
            {isExpanded ? 'Skjul dage' : 'Vis dage'}
            <ChevronDown size={14} aria-hidden="true" />
          </button>
        </div>
      </div>

      {isExpanded && (
        <div id={contentId} className="time-day-grid">
          {week.days.map((day) => (
            <DayCell
              key={day.date}
              day={day}
              isOutsideMonth={parseDate(day.date).getMonth() + 1 !== month}
              onOpenJob={onOpenJob}
            />
          ))}
        </div>
      )}
    </article>
  );
}

function DayCell({
  day,
  isOutsideMonth,
  onOpenJob,
}: {
  day: MyWorksheetDayResponse;
  isOutsideMonth: boolean;
  onOpenJob: (jobId: string) => void;
}) {
  const date = parseDate(day.date);
  const dayOfWeek = date.getDay();
  const isWeekend = dayOfWeek === 0 || dayOfWeek === 6;
  const isEmpty = day.entries.length === 0;

  const classNames = [
    'time-day-cell',
    isOutsideMonth ? 'muted' : '',
    isWeekend ? 'time-day-cell--weekend' : '',
    isEmpty ? 'time-day-cell--empty' : 'has-entries',
  ].filter(Boolean).join(' ');

  return (
    <div className={classNames}>
      <div className="time-day-head">
        <span>{DAY_FORMATTER.format(date)}</span>
        {day.totalHours !== 0 && <p>{formatNumber(day.totalHours)} total</p>}
      </div>

      <div className="time-entry-list">
        {day.entries.map((entry) => (
          <button
            key={`${entry.jobId}-${entry.workDate}-${entry.customerName}`}
            type="button"
            className="time-entry-card"
            onClick={() => onOpenJob(entry.jobId)}
            aria-label={`Åbn sag ${(entry.reportNumber || entry.jobId).toUpperCase()}`}
          >
            <div className="time-entry-top">
              <span className="job-number">SAG-{(entry.reportNumber || entry.jobId.slice(0, 4)).toUpperCase()}</span>
              <span className="time-entry-meta">
                <span>{formatNumber(entry.hoursWorked)} t</span>
                {entry.hasOutlay && <span className="time-entry-outlay"><ReceiptText size={12} /> Udlæg</span>}
              </span>
            </div>
            {entry.userDisplayName && <span className="time-entry-user">{entry.userDisplayName}</span>}
            <strong>{entry.customerName}</strong>
            {entry.customerAddress && (
              <span className="time-entry-address"><MapPin size={12} /> {entry.customerAddress}</span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}

function SummaryCard({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="time-summary-card">
      <span className="time-summary-icon">{icon}</span>
      <div>
        <span>{label}</span>
        <span>{value}</span>
      </div>
    </div>
  );
}

function addMonths(cursor: MonthCursor, delta: number): MonthCursor {
  const date = new Date(cursor.year, cursor.month - 1 + delta, 1);
  return { year: date.getFullYear(), month: date.getMonth() + 1 };
}

function getCurrentMonthCursor(): MonthCursor {
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}

function sameMonth(left: MonthCursor, right: MonthCursor) {
  return left.year === right.year && left.month === right.month;
}

function readTimerOverviewState(): TimerOverviewState | null {
  try {
    const raw = window.sessionStorage.getItem(TIMER_OVERVIEW_STATE_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<TimerOverviewState>;
    if (!isValidMonthCursor(parsed.cursor)) {
      return null;
    }

    return {
      cursor: parsed.cursor,
      expandedWeeks: Array.isArray(parsed.expandedWeeks) ? parsed.expandedWeeks.filter((week): week is string => typeof week === 'string') : [],
      scrollTop: typeof parsed.scrollTop === 'number' ? parsed.scrollTop : 0,
    };
  } catch {
    return null;
  }
}

function writeTimerOverviewState(cursor: MonthCursor, expandedWeeks: Set<string>, scrollTop: number) {
  window.sessionStorage.setItem(TIMER_OVERVIEW_STATE_KEY, JSON.stringify({
    cursor,
    expandedWeeks: Array.from(expandedWeeks),
    scrollTop,
  } satisfies TimerOverviewState));
}

function isValidMonthCursor(value: unknown): value is MonthCursor {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const cursor = value as Partial<MonthCursor>;
  const year = cursor.year;
  const month = cursor.month;
  return Number.isInteger(year) && Number.isInteger(month) && typeof month === 'number' && month >= 1 && month <= 12;
}

function getAppScrollElement() {
  return document.querySelector<HTMLElement>('.app-shell');
}

function getAppScrollTop() {
  return getAppScrollElement()?.scrollTop ?? 0;
}

function setAppScrollTop(scrollTop: number) {
  getAppScrollElement()?.scrollTo(0, scrollTop);
}

function formatMonth(cursor: MonthCursor) {
  return MONTH_FORMATTER.format(new Date(cursor.year, cursor.month - 1, 1));
}

function formatWeekRange(start: string, end: string) {
  return `${WEEK_RANGE_FORMATTER.format(parseDate(start))} - ${WEEK_RANGE_FORMATTER.format(parseDate(end))}`;
}

function formatNumber(value: number | string | null | undefined) {
  const number = Number(value ?? 0);
  return new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 }).format(number);
}

function countEntries(weeks: MyWorksheetWeekResponse[]) {
  return weeks.reduce((sum, week) => sum + week.days.reduce((daySum, day) => daySum + day.entries.length, 0), 0);
}

function countWeekEntries(week: MyWorksheetWeekResponse) {
  return week.days.reduce((sum, day) => sum + day.entries.length, 0);
}

function countUniqueEmployees(weeks: MyWorksheetWeekResponse[]): number {
  const names = new Set<string>();
  for (const week of weeks) {
    for (const day of week.days) {
      for (const entry of day.entries) {
        if (entry.userDisplayName) names.add(entry.userDisplayName);
      }
    }
  }
  return names.size;
}

function parseDate(value: string) {
  return new Date(`${value}T00:00:00`);
}

function getIsoWeek(value: string) {
  const date = parseDate(value);
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() + 3 - ((date.getDay() + 6) % 7));
  const week1 = new Date(date.getFullYear(), 0, 4);
  return 1 + Math.round(((date.getTime() - week1.getTime()) / 86400000 - 3 + ((week1.getDay() + 6) % 7)) / 7);
}

function getCurrentWeekStart(): string {
  const today = new Date();
  const dayOfWeek = today.getDay();
  // getDay(): 0 = Sunday, 1 = Monday, 2 = Tuesday, etc.
  // We want Monday of the current week
  const daysToMonday = dayOfWeek === 0 ? 6 : dayOfWeek - 1;
  const monday = new Date(today);
  monday.setDate(today.getDate() - daysToMonday);
  
  const yyyy = monday.getFullYear();
  const mm = String(monday.getMonth() + 1).padStart(2, '0');
  const dd = String(monday.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}
