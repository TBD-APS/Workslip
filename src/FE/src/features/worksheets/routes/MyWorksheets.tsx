import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { BriefcaseBusiness, ChevronDown, ChevronLeft, ChevronRight, MapPin, ReceiptText, Timer } from 'lucide-react';
import { ErrorState } from '../../../components/ErrorState';
import { CopyAddressButton } from '../../../components/CopyAddressButton';
import { apiClient } from '../../../lib/axios';
import { abbreviateName } from '../../../lib/formatUtils';
import { formatDayMonth, formatMonthYear, formatWeekdayDay } from '../../../lib/presentation/date';
import { formatNumber as formatPresentationNumber } from '../../../lib/presentation/number';
import { compareUiText } from '../../../lib/presentation/text';
import { useIsAdmin } from '../../../providers/permissions';
import { useAppScrollRestoreKey } from '../../../hooks/useAppRouteScroll';
import { AdminHoursExport } from '../components/AdminHoursExport';
import { getWorksheetEntryIdentity } from '../utils/worksheetEntryIdentity';
import type {
  MyWorksheetDayResponse,
  MyWorksheetEntryResponse,
  MyWorksheetWeekResponse,
  MyWorksheetsMonthResponse,
} from '../worksheetOverviewTypes';
import './MyWorksheets.css';

type MonthCursor = { year: number; month: number };
type TimerOverviewState = {
  cursor: MonthCursor;
  expandedWeeks: string[];
  scrollTop: number;
};
type TimerDesktopView = 'ledger' | 'week';

type AdminUserWeek = {
  displayName: string;
  totalHours: number;
  days: Map<string, { hours: number; entries: MyWorksheetEntryResponse[] }>;
};

type AccountingDocumentResponse = {
  documentId: string;
  documentNumber: string;
  type: string;
  amount: number;
  date: string;
  status: string;
  externalLink: string;
};

const TIMER_OVERVIEW_STATE_KEY = 'workslip.timerOverviewState';

function AdminWeeklyOverview({
  data,
  currentWeekStart,
  selectedUserId,
  setSelectedUserId,
  onOpenJob,
}: {
  data: MyWorksheetsMonthResponse;
  currentWeekStart: string;
  selectedUserId: string | null;
  setSelectedUserId: (id: string | null) => void;
  onOpenJob: (jobId: string) => void;
}) {
  const users = useMemo(() => {
    const userMap = new Map<string, { displayName: string }>();
    data.weeks.forEach((week) => {
      week.days.forEach((day) => {
        day.entries.forEach((entry) => {
          const userId = getWorksheetEntryIdentity(entry);
          userMap.set(userId, {
            displayName: entry.userDisplayName?.trim() || 'Ukendt medarbejder',
          });
        });
      });
    });
    return Array.from(userMap.entries()).sort((a, b) => compareUiText(a[1].displayName, b[1].displayName));
  }, [data]);

  return (
    <section id="timer-week-overview" className="admin-weekly-overview timer-admin-week-view">
      <div className="admin-overview-toolbar">
        <label htmlFor="admin-user-filter">Medarbejder</label>
        <select
          id="admin-user-filter"
          value={selectedUserId ?? ''}
          onChange={(e) => setSelectedUserId(e.target.value || null)}
          className="admin-user-select"
        >
          <option value="">Alle medarbejdere</option>
          {users.map(([id, user]) => (
            <option key={id} value={id}>{user.displayName}</option>
          ))}
        </select>
      </div>

      {selectedUserId ? (
        <AdminEmployeeDetail
          data={data}
          userId={selectedUserId}
          userName={users.find(([id]) => id === selectedUserId)?.[1].displayName ?? 'Ukendt medarbejder'}
          onOpenJob={onOpenJob}
        />
      ) : (
        <AdminWeeklyMatrix data={data} currentWeekStart={currentWeekStart} />
      )}
    </section>
  );
}

function AdminEmployeeDetail({
  data,
  userId,
  userName,
  onOpenJob,
}: {
  data: MyWorksheetsMonthResponse;
  userId: string;
  userName: string;
  onOpenJob: (jobId: string) => void;
}) {
  const userStats = useMemo(() => {
    let totalHours = 0;
    const days = new Map<string, { date: string; hours: number; entries: MyWorksheetEntryResponse[] }>();

    data.weeks.forEach((week) => {
      week.days.forEach((day) => {
        const userEntries = day.entries.filter((e) => getWorksheetEntryIdentity(e) === userId);
        if (userEntries.length > 0) {
          const dayHours = userEntries.reduce((sum, e) => sum + Number(e.hoursWorked), 0);
          days.set(day.date, {
            date: day.date,
            hours: dayHours,
            entries: userEntries,
          });
          totalHours += dayHours;
        }
      });
    });

    return { totalHours, days: Array.from(days.values()).sort((a, b) => a.date.localeCompare(b.date)) };
  }, [data, userId]);

  const docsQuery = useQuery({
    queryKey: ['accounting-docs', userId, data.monthStart, data.monthEnd],
    queryFn: async () => {
      const res = await apiClient.get<AccountingDocumentResponse[]>(`/api/worksheets/all/documents/${userId}`, {
        params: { startDate: data.monthStart, endDate: data.monthEnd },
      });
      return res.data;
    },
  });

  return (
    <div className="admin-employee-detail">
      <header className="admin-employee-detail-header">
        <div>
          <h2>{userName}</h2>
          <p>Økonomisk overblik for valgte periode</p>
        </div>
        <div className="admin-employee-detail-total">
          <span className="label">Total timer</span>
          <strong className="value">{formatNumeric(userStats.totalHours)} t</strong>
        </div>
      </header>

      <div className="admin-employee-content-grid">
        <div className="admin-employee-days">
          {userStats.days.map((day) => (
            <div key={day.date} className="admin-employee-day">
              <div className="admin-employee-day-header">
                <span className="admin-employee-day-date">{formatWeekdayDay(parseDate(day.date))}</span>
                <span className="admin-employee-day-hours">{formatNumeric(day.hours)} t</span>
              </div>
              <div className="admin-employee-entries">
                {day.entries.map((entry) => (
                  <div
                    key={entry.jobId}
                    className="admin-employee-entry"
                    onClick={() => onOpenJob(entry.jobId)}
                    onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') onOpenJob(entry.jobId); }}
                    role="link"
                    tabIndex={0}
                  >
                    <div className="admin-employee-entry-main">
                      <span className="admin-employee-entry-case">SAG-{(entry.reportNumber || entry.jobId.slice(0, 4)).toUpperCase()}</span>
                      <span className="admin-employee-entry-customer">{entry.customerName}</span>
                    </div>
                    <span className="admin-employee-entry-hours">{formatNumeric(entry.hoursWorked)} t</span>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>

        <div className="admin-employee-docs">
           <div className="admin-employee-docs-header">
             <h3 style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
               Eksterne bilag & fakturaer
               <span className="sync-health-indicator" title="Tjekker sync status...">
                 <div className="health-dot" />
                 <span className="health-text">Sjekker...</span>
               </span>
             </h3>
             <span className="badge">Integration aktiv</span>
           </div>
          <div className="admin-employee-docs-list">
            {docsQuery.isLoading ? (
              <div className="docs-skeleton">Henter dokumenter...</div>
            ) : docsQuery.data?.length === 0 ? (
              <div className="docs-empty">Ingen eksterne dokumenter fundet.</div>
            ) : (
              docsQuery.data?.map((doc) => (
                <div key={doc.documentId} className="admin-employee-doc-item">
                  <div className="admin-employee-doc-main">
                    <span className="admin-employee-doc-number">{doc.documentNumber}</span>
                    <span className="admin-employee-doc-type">{doc.type === 'Invoice' ? 'Faktura' : 'Bilag'}</span>
                  </div>
                    <div className="admin-employee-doc-meta">
                      <span className="admin-employee-doc-date">{doc.date}</span>
                      <span className="admin-employee-doc-amount">{formatNumeric(doc.amount)} kr.</span>
                      <a href={doc.externalLink} target="_blank" rel="noopener noreferrer" className="admin-employee-doc-link">Original</a>
                    </div>
                </div>
              ))
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function AdminWeeklyMatrix({ data, currentWeekStart }: { data: MyWorksheetsMonthResponse; currentWeekStart: string }) {
  return (
    <div className="admin-weekly-matrix">
      {data.weeks.map((week) => {
        const isCurrentWeek = week.weekStart === currentWeekStart;
        const users = new Map<string, AdminUserWeek>();

        week.days.forEach((day) => {
          day.entries.forEach((entry) => {
            const hours = Number(entry.hoursWorked);
            const userId = getWorksheetEntryIdentity(entry);
            const existing = users.get(userId);
            const user = existing ?? {
              displayName: entry.userDisplayName?.trim() || 'Ukendt medarbejder',
              totalHours: 0,
              days: new Map(),
            };

            const dayData = user.days.get(day.date) ?? { hours: 0, entries: [] };
            dayData.hours += hours;
            dayData.entries.push(entry);
            user.days.set(day.date, dayData);
            user.totalHours += hours;

            if (!existing) users.set(userId, user);
          });
        });

        const orderedUsers = Array.from(users.entries()).sort((left, right) => {
          const nameComparison = compareUiText(left[1].displayName, right[1].displayName);
          return nameComparison !== 0 ? nameComparison : left[0].localeCompare(right[0]);
        });

        return (
          <div
            id={`timer-week-matrix-${week.weekStart}`}
            key={week.weekStart}
            className={`admin-week-card ${isCurrentWeek ? 'is-current' : ''}`}
          >
            <table className="admin-week-table">
              <thead>
                <tr>
                  <th className="admin-col-name admin-row-label">
                    <span className="admin-week-info">{formatWeekRange(week.weekStart, week.weekEnd)}</span>
                    <span className="admin-week-number">Uge {getIsoWeek(week.weekStart)}{isCurrentWeek && <span className="current-week-badge">Nu</span>}</span>
                  </th>
                  <th className="admin-col-total">I alt</th>
                  {week.days.map((day) => {
                    const date = parseDate(day.date);
                    const dayOfWeek = date.getDay();
                    const isWeekend = dayOfWeek === 0 || dayOfWeek === 6;
                    const weekday = (formatWeekdayDay(date) ?? '').split(' ')[0];
                    return (
                      <th key={day.date} className={`admin-col-day ${isWeekend ? 'admin-col-weekend' : ''}`}>
                        <span className="admin-day-short">{weekday}</span>
                        <span className="admin-day-num">{date.getDate()}</span>
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {orderedUsers.map(([userId, user]) => (
                  <tr key={userId} className="admin-user-row">
                    <td className="admin-col-name admin-user-name">{user.displayName}</td>
                    <td className="admin-col-total admin-user-total">{formatNumeric(user.totalHours)}</td>
                    {week.days.map((day) => {
                      const dayData = user.days.get(day.date);
                      const date = parseDate(day.date);
                      const dayOfWeek = date.getDay();
                      const isWeekend = dayOfWeek === 0 || dayOfWeek === 6;
                      return (
                        <td key={day.date} className={`admin-col-day admin-col-hours ${isWeekend ? 'admin-col-weekend' : ''}`}>
                          {dayData ? formatNumeric(dayData.hours) : '—'}
                        </td>
                      );
                    })}
                  </tr>
                ))}
                <tr className="admin-row-total">
                  <td className="admin-col-name">I alt pr. dag</td>
                  <td className="admin-col-total">{formatNumeric(week.totalHours)}</td>
                  {week.days.map((day) => {
                    const date = parseDate(day.date);
                    const dayOfWeek = date.getDay();
                    const isWeekend = dayOfWeek === 0 || dayOfWeek === 6;
                    return (
                      <td key={day.date} className={`admin-col-day admin-col-hours ${isWeekend ? 'admin-col-weekend' : ''}`}>
                        {formatNumeric(day.totalHours)}
                      </td>
                    );
                  })}
                </tr>
              </tbody>
            </table>
          </div>
        );
      })}
    </div>
  );
}

function TimerDesktopOverview({
  data,
  isAdmin,
  currentWeekStart,
  onOpenJob,
}: {
  data: MyWorksheetsMonthResponse;
  isAdmin: boolean;
  currentWeekStart: string;
  onOpenJob: (jobId: string) => void;
}) {
  const [view, setView] = useState<TimerDesktopView>('ledger');
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);

  return (
    <div className="timer-desktop-overview">
      {isAdmin && (
        <div className="timer-view-toolbar">
          <div className="timer-view-switcher" role="group" aria-label="Vælg timevisning">
            <button
              id="timer-view-ledger"
              type="button"
              className={`timer-view-button ${view === 'ledger' ? 'is-active' : ''}`}
              aria-pressed={view === 'ledger'}
              onClick={() => {
                setView('ledger');
                setSelectedUserId(null);
              }}
            >
              Registreringer
            </button>
            <button
              id="timer-view-week"
              type="button"
              className={`timer-view-button ${view === 'week' ? 'is-active' : ''}`}
              aria-pressed={view === 'week'}
              onClick={() => setView('week')}
            >
              Økonomioversigt
            </button>
          </div>
        </div>
      )}

      {view === 'ledger' || !isAdmin ? (
        <TimerLedger data={data} isAdmin={isAdmin} currentWeekStart={currentWeekStart} onOpenJob={onOpenJob} />
      ) : (
        <AdminWeeklyOverview
          data={data}
          currentWeekStart={currentWeekStart}
          selectedUserId={selectedUserId}
          setSelectedUserId={setSelectedUserId}
          onOpenJob={onOpenJob}
        />
      )}
    </div>
  );
}

function TimerLedger({
  data,
  isAdmin,
  currentWeekStart,
  onOpenJob,
}: {
  data: MyWorksheetsMonthResponse;
  isAdmin: boolean;
  currentWeekStart: string;
  onOpenJob: (jobId: string) => void;
}) {
  return (
    <section id="timer-ledger" className={`timer-ledger ${isAdmin ? 'is-admin' : ''}`} aria-label="Økonomioversigt">
      <div className="timer-ledger-columns" aria-hidden="true">
        <span>Dato</span>
        {isAdmin && <span>Medarbejder</span>}
        <span>Sag</span>
        <span>Kunde</span>
        <span className="is-numeric">Timer</span>
        <span className="is-numeric">Udlæg</span>
      </div>

      {data.weeks.map((week) => {
        const entries = week.days.flatMap((day) => day.entries.map((entry) => ({ day, entry })));
        if (entries.length === 0) return null;
        const isCurrentWeek = week.weekStart === currentWeekStart;

        return (
          <section id={`timer-ledger-week-${week.weekStart}`} key={week.weekStart} className="timer-ledger-week">
            <header className="timer-ledger-week-header">
              <div className="timer-ledger-week-title">
                <strong>Uge {getIsoWeek(week.weekStart)}{isCurrentWeek ? ' · Nu' : ''}</strong>
                <span>{formatWeekRange(week.weekStart, week.weekEnd)}</span>
              </div>
              <span className="timer-ledger-week-total">{formatNumeric(week.totalHours)} t</span>
            </header>

            {entries.map(({ day, entry }) => {
              const entryId = getTimerEntryDomId(entry);
              const openEntry = () => onOpenJob(entry.jobId);
              return (
                <div
                  id={entryId}
                  key={entryId}
                  className="timer-ledger-row"
                  onClick={openEntry}
                  onKeyDown={(event) => {
                    if (event.target !== event.currentTarget) return;
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      openEntry();
                    }
                  }}
                  role="link"
                  tabIndex={0}
                  aria-label={`Åbn sag ${(entry.reportNumber || entry.jobId).toUpperCase()}`}
                >
                  <span className="timer-ledger-date">{formatWeekdayDay(parseDate(day.date))}</span>
                  {isAdmin && (
                    <span className="timer-ledger-person" title={entry.userDisplayName?.trim() || 'Ukendt medarbejder'}>
                      {entry.userDisplayName?.trim() || 'Ukendt medarbejder'}
                    </span>
                  )}
                  <span className="timer-ledger-case">SAG-{(entry.reportNumber || entry.jobId.slice(0, 4)).toUpperCase()}</span>
                  <span className="timer-ledger-customer">
                    <strong>{entry.customerName}</strong>
                    {entry.customerAddress && (
                      <span className="timer-ledger-address">
                        <MapPin size={12} aria-hidden="true" />
                        <span>{entry.customerAddress}</span>
                        <CopyAddressButton address={entry.customerAddress} />
                      </span>
                    )}
                  </span>
                  <span className="timer-ledger-hours">{formatNumeric(entry.hoursWorked)} t</span>
                  <span className={`timer-ledger-outlay ${entry.hasOutlay ? 'has-outlay' : ''}`}>
                    {entry.hasOutlay ? <><ReceiptText size={13} aria-hidden="true" /> Ja</> : '—'}
                  </span>
                </div>
              );
            })}
          </section>
        );
      })}
    </section>
  );
}

export function MyWorksheets() {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const restoreScrollKey = useAppScrollRestoreKey();
  const [savedState] = useState(readTimerOverviewState);
  const restoredScrollKey = useRef<string | null>(null);
  const [cursor, setCursor] = useState<MonthCursor>(() => savedState?.cursor ?? getCurrentMonthCursor());
  const [expandedWeeks, setExpandedWeeks] = useState<Set<string>>(() => {
    const saved = savedState?.expandedWeeks;
    if (saved && saved.length > 0) {
      return new Set(saved);
    }
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
  const entryCount = data ? countEntries(data.weeks) : 0;

  useEffect(() => {
    writeTimerOverviewState(cursor, expandedWeeks, getAppScrollTop());
  }, [cursor, expandedWeeks]);

  useEffect(() => {
    if (!data || !restoreScrollKey || restoredScrollKey.current === restoreScrollKey) {
      return;
    }

    const scrollTop = savedState?.scrollTop ?? 0;
    const frame = requestAnimationFrame(() => {
      setAppScrollTop(scrollTop);
      restoredScrollKey.current = restoreScrollKey;
    });
    return () => cancelAnimationFrame(frame);
  }, [data, restoreScrollKey, savedState?.scrollTop]);

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
    <div id="timer-page" className={`page-container time-overview-page ${isAdmin ? 'time-overview-page--admin' : ''}`}>
      <div className="page-header time-overview-header">
        <div>
          <h2>{isAdmin ? 'Alles økonomi' : 'Min økonomi'}</h2>
          <p className="subtitle">Ugentlig økonomioversigt over sager, timer og udlæg</p>
        </div>
        <div className="time-month-controls">
          <div id="timer-month-switcher" className="time-month-switcher" aria-label="Vælg måned">
            <button
              id="timer-month-previous"
              type="button"
              className="btn-icon time-month-button"
              onClick={() => selectMonth(addMonths(cursor, -1))}
              aria-label="Forrige måned"
            >
              <ChevronLeft size={20} />
            </button>
            <span>{monthLabel}</span>
            <button
              id="timer-month-next"
              type="button"
              className="btn-icon time-month-button"
              onClick={() => selectMonth(addMonths(cursor, 1))}
              aria-label="Næste måned"
            >
              <ChevronRight size={20} />
            </button>
          </div>
          <button
            id="timer-month-current"
            type="button"
            className="time-today-button"
            onClick={() => selectMonth(getCurrentMonthCursor())}
            disabled={isCurrentMonth}
          >
            Til nuværende måned
          </button>
        </div>
      </div>

      {data && (
        <section id="timer-summary" className="timer-summary-strip" aria-label="Månedsoversigt">
          <div className="timer-summary-item">
            <span>Timer</span>
            <strong>{formatNumeric(data.totalHours)} t</strong>
          </div>
          <div className="timer-summary-item">
            <span>Registreringer</span>
            <strong>{formatNumeric(entryCount)}</strong>
          </div>
          <div className="timer-summary-item">
            <span>Udlæg</span>
            <strong>{formatNumeric(data.outlayCount)}</strong>
          </div>
        </section>
      )}

      {isAdmin && data && <AdminHoursExport data={data} monthLabel={monthLabel} />}

      {query.isLoading && (
        <>
          <div className="timer-desktop-skeleton" aria-hidden="true" />
          <div className="time-week-list timer-mobile-overview" aria-hidden="true">
            <div className="time-week-card time-week-skeleton" />
            <div className="time-week-card time-week-skeleton" />
          </div>
        </>
      )}

      {query.isError && (
        <ErrorState message={isAdmin ? 'Kunne ikke hente timer.' : 'Kunne ikke hente dine timer.'} onRetry={() => query.refetch()} />
      )}

      {data && (
        <>
          {entryCount === 0 && (
            <div className="empty-state">
              <p>{isAdmin ? `Ingen timer registreret for nogen medarbejdere i ${monthLabel}.` : `Ingen timer registreret i ${monthLabel}.`}</p>
            </div>
          )}

          {entryCount > 0 && (
            <>
              <TimerDesktopOverview
                data={data}
                isAdmin={isAdmin}
                currentWeekStart={getCurrentWeekStart()}
                onOpenJob={openJob}
              />
              <section id="timer-mobile-overview" className="time-week-list timer-mobile-overview" aria-label="Ugentligt timeoverblik">
                {data.weeks.map((week) => (
                  <WeekCard
                    key={week.weekStart}
                    week={week}
                    month={data.month}
                    isCurrentWeek={week.weekStart === getCurrentWeekStart()}
                    isExpanded={expandedWeeks.has(week.weekStart)}
                    onToggle={() => toggleWeek(week.weekStart)}
                    onOpenJob={openJob}
                  />
                ))}
              </section>
            </>
          )}
        </>
      )}
    </div>
  );
}

function WeekCard({
  week,
  month,
  isCurrentWeek,
  isExpanded,
  onToggle,
  onOpenJob,
}: {
  week: MyWorksheetWeekResponse;
  month: number;
  isCurrentWeek: boolean;
  isExpanded: boolean;
  onToggle: () => void;
  onOpenJob: (jobId: string) => void;
}) {
  const entryCount = countWeekEntries(week);
  const contentId = `timer-mobile-week-content-${week.weekStart}`;

  return (
    <article
      id={`timer-mobile-week-${week.weekStart}`}
      className={`time-week-card ${isCurrentWeek ? 'is-current' : ''} ${isExpanded ? 'is-expanded' : ''}`}
    >
      <div className="time-week-header">
        <div>
          <span className="job-number">{formatWeekRange(week.weekStart, week.weekEnd)} | Uge {getIsoWeek(week.weekStart)} </span>
          {isCurrentWeek && <span className="current-week-badge">Nu</span>}
        </div>
        <div className="time-week-totals">
          <span><Timer size={14} /> {formatNumeric(week.totalHours)} t</span>
          <span><ReceiptText size={14} /> {week.outlayCount}</span>
          <span><BriefcaseBusiness size={14} /> {entryCount}</span>
          <button
            id={`timer-mobile-week-toggle-${week.weekStart}`}
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
        <span>{formatWeekdayDay(date)}</span>
        {day.totalHours !== 0 && <p>{formatNumeric(day.totalHours)} total</p>}
      </div>

      <div className="time-entry-list">
        {day.entries.map((entry) => {
          const openEntry = () => onOpenJob(entry.jobId);
          return (
            <div
              key={`${entry.jobId}-${entry.workDate}-${entry.customerName}`}
              className="time-entry-card"
              onClick={openEntry}
              onKeyDown={(event) => {
                if (event.target !== event.currentTarget) return;
                if (event.key === 'Enter' || event.key === ' ') openEntry();
              }}
              role="link"
              tabIndex={0}
              aria-label={`Åbn sag ${(entry.reportNumber || entry.jobId).toUpperCase()}`}
            >
              <div className="time-entry-top">
                <span className="job-number">SAG-{(entry.reportNumber || entry.jobId.slice(0, 4)).toUpperCase()}</span>
                <span className="time-entry-meta">
                  <span>{formatNumeric(entry.hoursWorked)} t</span>
                  {entry.hasOutlay && <span className="time-entry-outlay"><ReceiptText size={12} /> Udlæg</span>}
                </span>
              </div>
              {entry.userDisplayName && <span className="time-entry-user">{abbreviateName(entry.userDisplayName)}</span>}
              <strong>{entry.customerName}</strong>
              {entry.customerAddress && (
                <span className="time-entry-address">
                  <MapPin size={12} />
                  <span>{entry.customerAddress}</span>
                  <CopyAddressButton address={entry.customerAddress} />
                </span>
              )}
            </div>
          );
        })}
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
  return formatMonthYear(new Date(cursor.year, cursor.month - 1, 1)) ?? '';
}

function formatWeekRange(start: string, end: string) {
  return `${formatDayMonth(parseDate(start)) ?? start} - ${formatDayMonth(parseDate(end)) ?? end}`;
}

function formatNumeric(value: number | string | null | undefined) {
  return formatPresentationNumber(Number(value ?? 0), { maximumFractionDigits: 2 });
}

function countEntries(weeks: MyWorksheetWeekResponse[]) {
  return weeks.reduce((sum, week) => sum + week.days.reduce((daySum, day) => daySum + day.entries.length, 0), 0);
}

function countWeekEntries(week: MyWorksheetWeekResponse) {
  return week.days.reduce((sum, day) => sum + day.entries.length, 0);
}

function getTimerEntryDomId(entry: MyWorksheetEntryResponse) {
  return `timer-ledger-entry-${entry.jobId}-${entry.userId}-${entry.workDate.slice(0, 10)}`;
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
  const daysToMonday = dayOfWeek === 0 ? 6 : dayOfWeek - 1;
  const monday = new Date(today);
  monday.setDate(today.getDate() - daysToMonday);

  const yyyy = monday.getFullYear();
  const mm = String(monday.getMonth() + 1).padStart(2, '0');
  const dd = String(monday.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}
