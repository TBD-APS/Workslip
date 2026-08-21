import { UI_LOCALE } from './locale';

const UI_COLLATOR = new Intl.Collator(UI_LOCALE, { sensitivity: 'base' });

export function toUiLowerCase(value: string): string {
  return value.toLocaleLowerCase(UI_LOCALE);
}

export function toUiUpperCase(value: string): string {
  return value.toLocaleUpperCase(UI_LOCALE);
}

export function compareUiText(left: string, right: string): number {
  return UI_COLLATOR.compare(left, right);
}
