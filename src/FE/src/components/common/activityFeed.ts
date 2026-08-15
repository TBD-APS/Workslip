export function formatRelativeActivityTime(value: string) {
  const date = new Date(value);
  const timestamp = date.getTime();

  if (!Number.isFinite(timestamp)) return '';

  const diffMs = Date.now() - timestamp;
  const diffMinutes = Math.max(0, Math.round(diffMs / 60_000));

  if (diffMinutes < 1) return 'Nu';
  if (diffMinutes < 60) return `${diffMinutes} min.`;

  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours} t.`;

  const diffDays = Math.round(diffHours / 24);
  if (diffDays < 7) return diffDays === 1 ? 'I går' : `${diffDays} dage`;

  return date.toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'short',
  });
}

export function formatActivityDateSection(value: string) {
  const date = new Date(value);
  const timestamp = date.getTime();

  if (!Number.isFinite(timestamp)) return 'Tidligere';

  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const eventDay = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const diffDays = Math.round((today.getTime() - eventDay.getTime()) / 86_400_000);

  if (diffDays === 0) return 'I dag';
  if (diffDays === 1) return 'I går';

  return date.toLocaleDateString('da-DK', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
  });
}

export function getActivityInitials(name: string | null | undefined) {
  const normalized = name?.trim();
  if (!normalized) return 'WS';

  const words = normalized.split(/\s+/).filter(Boolean);
  if (words.length === 1) return words[0].slice(0, 2).toLocaleUpperCase('da-DK');

  return `${words[0][0]}${words[words.length - 1][0]}`.toLocaleUpperCase('da-DK');
}
