const NUMBER_FORMATTER = new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 });

export function parseNullableNumber(value: number | string | null): number {
  if (value === null) return 0;
  const parsed = typeof value === 'number' ? value : Number(value.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : 0;
}

export function formatNumber(value: number | string | null): string {
  return NUMBER_FORMATTER.format(parseNullableNumber(value));
}

export function formatUnit(value: number, singular: string, plural: string): string {
  return Math.abs(value) === 1 ? singular : plural;
}

export function capitalize(value: string): string {
  if (value.length === 0) return value;
  return `${value[0].toLocaleUpperCase('da-DK')}${value.slice(1)}`;
}

export function abbreviateName(name: string | null | undefined): string {
  if (!name) return '';
  const parts = name.trim().split(/\s+/);
  if (parts.length <= 1) return name;
  return `${parts[0]} ${parts[parts.length - 1][0].toUpperCase()}.`;
}

export type DetailPair = { label: string; value: string | null | undefined };

export function hasText(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

export function compactPairs(items: DetailPair[]): { label: string; value: string }[] {
  return items.filter((item): item is { label: string; value: string } => hasText(item.value));
}
