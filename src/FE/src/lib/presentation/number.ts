import { UI_LOCALE } from './locale';

/** Locale-aware numeric presentation. Feature semantics may choose precision/unit options centrally through this boundary. */
export function formatNumber(value: number, options?: Intl.NumberFormatOptions): string {
  return new Intl.NumberFormat(UI_LOCALE, options).format(value);
}

export function formatFixedNumber(value: number, fractionDigits: number): string {
  return formatNumber(value, {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  });
}
