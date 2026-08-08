export function getWorksheetEntryIdentity(entry: { userId?: string | null; userDisplayName?: string | null }): string {
  const stableUserId = typeof entry.userId === 'string' ? entry.userId.trim() : '';
  if (stableUserId) return stableUserId;

  // Frontend and backend deploy independently. During a rolling deployment the
  // browser can briefly receive the legacy worksheet shape without userId.
  // Preserve the previous name-based grouping semantics instead of crashing.
  const legacyName = entry.userDisplayName?.trim().toLocaleLowerCase('da-DK') || 'ukendt medarbejder';
  return `legacy:${legacyName}`;
}
