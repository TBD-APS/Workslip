const DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: '2-digit', month: '2-digit', year: 'numeric' });
const DATE_FORMATTER_LONG = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'long', year: 'numeric' });

export function formatDate(value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return DATE_FORMATTER.format(date);
}

export function formatDateLong(value: string | null | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return DATE_FORMATTER_LONG.format(date);
}
