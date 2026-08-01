const DATE_FORMATTER_LONG = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short', year: 'numeric' });
const DATE_TIME_FORMATTER_SHORT = new Intl.DateTimeFormat('da-DK', { dateStyle: 'short', timeStyle: 'short' });

export function formatDateLong(value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return DATE_FORMATTER_LONG.format(date);
}

export function formatDateTimeShort(value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return DATE_TIME_FORMATTER_SHORT.format(date);
}
