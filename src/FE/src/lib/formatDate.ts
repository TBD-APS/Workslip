const DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
});
const DATE_TIME_FORMATTER_SHORT = new Intl.DateTimeFormat('da-DK', { dateStyle: 'short', timeStyle: 'short' });

function formatWith(formatter: Intl.DateTimeFormat, value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return formatter.format(date);
}

/**
 * Canonical Workslip date-only presentation for user-visible UI.
 * Example (da-DK): 17. aug. 2026
 */
export function formatDate(value: string | null | undefined): string | null {
  return formatWith(DATE_FORMATTER, value);
}

/** @deprecated Use formatDate for all user-visible date-only values. */
export function formatDateLong(value: string | null | undefined): string | null {
  return formatDate(value);
}

/** @deprecated Use formatDate for all user-visible date-only values. */
export function formatDateShort(value: string | null | undefined): string | null {
  return formatDate(value);
}

export function formatDateTimeShort(value: string | null | undefined): string | null {
  return formatWith(DATE_TIME_FORMATTER_SHORT, value);
}
