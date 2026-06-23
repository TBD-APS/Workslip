import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, BriefcaseBusiness, ChevronDown, ChevronLeft, ChevronRight, MapPin, ReceiptText, Timer } from 'lucide-react';
import { apiClient } from '../../../lib/axios';

type MyWorksheetEntryResponse = {
  workDate: string;
  jobId: string;
  reportNumber: string | null;
  customerName: string;
  customerAddress: string | null;
  hoursWorked: number | string;
  hasOutlay: boolean;
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

const MONTH_FORMATTER = new Intl.DateTimeFormat('da-DK', { month: 'long', year: 'numeric' });
const DAY_FORMATTER = new Intl.DateTimeFormat('da-DK', { weekday: 'short', day: 'numeric' });
const WEEK_RANGE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short' });

export function MyWorksheets() {
  const navigate = useNavigate();
  const savedState = useRef(readTimerOverviewState());
  const hasRestoredScroll = useRef(false);
  const [cursor, setCursor] = useState<MonthCursor>(() => savedState.current?.cursor ?? getCurrentMonthCursor());
  const [expandedWeeks, setExpandedWeeks] = useState<Set<string>>(() => new Set(savedState.current?.expandedWeeks ?? []));

  const query = useQuery({
    queryKey: ['worksheets', 'my', cursor],
    queryFn: async () => (await apiClient.get('/api/worksheets/my', { params: cursor })) as MyWorksheetsMonthResponse,
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
    setExpandedWeeks(new Set());
    writeTimerOverviewState(nextCursor, new Set(), 0);
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
    <div className="page-container time-overview-page">
      <div className="page-header time-overview-header">
        <div>
          <h2>Mine timer</h2>
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
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente dine timer.</p>
          <button type="button" className="btn btn-primary" onClick={() => query.refetch()}>
            Prøv igen
          </button>
        </div>
      )}

      {data && (
        <>
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

          {countEntries(data.weeks) === 0 && (
            <div className="empty-state">
              <p>Ingen timer registreret i {monthLabel}.</p>
            </div>
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
  return (
    <div className={`time-day-cell ${isOutsideMonth ? 'muted' : ''} ${day.entries.length > 0 ? 'has-entries' : ''}`}>
      <div className="time-day-head">
        <span>{DAY_FORMATTER.format(parseDate(day.date))}</span>
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
