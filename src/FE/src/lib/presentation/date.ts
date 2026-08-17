import { UI_LOCALE } from './locale';

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

function formatWith(formatter: Intl.DateTimeFormat, value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return formatter.format(date);
}

/** Canonical Workslip date-only presentation. Example: 17. aug. 2026. */
export function formatDate(value: string | null | undefined): string | null {
  return formatWith(DATE_FORMATTER, value);
}

/** Canonical Workslip date+time presentation using the same date style. */
export function formatDateTime(value: string | null | undefined): string | null {
  return formatWith(DATE_TIME_FORMATTER, value);
}
