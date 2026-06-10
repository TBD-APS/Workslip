import { useMemo } from 'react';

export function useSearch<T>(
  items: T[],
  searchTerm: string,
  predicate: (item: T, term: string) => boolean,
): T[] {
  return useMemo(() => {
    const trimmed = searchTerm.trim().toLowerCase();
    if (!trimmed) return items;
    return items.filter((item) => predicate(item, trimmed));
  }, [items, searchTerm, predicate]);
}
