import { formatDate, formatDateTime } from './presentation/date';

export { formatDate };

/** @deprecated Use formatDate for all user-visible date-only values. */
export function formatDateLong(value: string | null | undefined): string | null {
  return formatDate(value);
}

/** @deprecated Use formatDate for all user-visible date-only values. */
export function formatDateShort(value: string | null | undefined): string | null {
  return formatDate(value);
}

/** @deprecated Use formatDateTime for user-visible timestamps. */
export function formatDateTimeShort(value: string | null | undefined): string | null {
  return formatDateTime(value);
}

export { formatDateTime };
