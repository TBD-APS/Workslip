import { UI_LOCALE } from './locale';

export type DateInput = string | number | Date | null | undefined;

const DATE_FORMATTER = new Intl.DateTimeFormat(UI_LOCALE, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
});

const DATE_TIME_FORMATTER = new Intl.DateTimeFormat(UI_LOCALE, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

const MONTH_YEAR_FORMATTER = new Intl.DateTimeFormat(UI_LOCALE, { month: 'short', year: 'numeric' });
const WEEKDAY_DAY_FORMATTER = new Intl.DateTimeFormat(UI_LOCALE, { weekday: 'short', day: 'numeric' });
const DAY_MONTH_FORMATTER = new Intl.DateTimeFormat(UI_LOCALE, { day: 'numeric', month: 'short' });

function formatWith(formatter: Intl.DateTimeFormat, value: DateInput): string | null {
  if (value == null || value === '') return null;
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return typeof value === 'string' ? value : null;
  return formatter.format(date);
}

/** Canonical Workslip standalone date presentation. Example: 17. aug. 2026. */
export function formatDate(value: DateInput): string | null {
  return formatWith(DATE_FORMATTER, value);
}

/** Canonical Workslip date+time presentation using the same textual date style. */
export function formatDateTime(value: DateInput): string | null {
  return formatWith(DATE_TIME_FORMATTER, value);
}

/** Calendar/header presentation. Example: aug. 2026. */
export function formatMonthYear(value: DateInput): string | null {
  return formatWith(MONTH_YEAR_FORMATTER, value);
}

/** Compact calendar day presentation. */
export function formatWeekdayDay(value: DateInput): string | null {
  return formatWith(WEEKDAY_DAY_FORMATTER, value);
}

/** Compact date range endpoint presentation. Example: 17. aug. */
export function formatDayMonth(value: DateInput): string | null {
  return formatWith(DAY_MONTH_FORMATTER, value);
}
