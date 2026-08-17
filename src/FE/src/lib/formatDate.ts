const DATE_FORMATTER_LONG = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short', year: 'numeric' });
const DATE_FORMATTER_SHORT = new Intl.DateTimeFormat('da-DK', { dateStyle: 'short' });
const DATE_TIME_FORMATTER_SHORT = new Intl.DateTimeFormat('da-DK', { dateStyle: 'short', timeStyle: 'short' });

function formatWith(formatter: Intl.DateTimeFormat, value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return formatter.format(date);
}

export function formatDateLong(value: string | null | undefined): string | null {
  return formatWith(DATE_FORMATTER_LONG, value);
}

export function formatDateShort(value: string | null | undefined): string | null {
  return formatWith(DATE_FORMATTER_SHORT, value);
}

export function formatDateTimeShort(value: string | null | undefined): string | null {
  return formatWith(DATE_TIME_FORMATTER_SHORT, value);
}
