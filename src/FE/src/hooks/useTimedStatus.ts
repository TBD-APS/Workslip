import { useCallback, useEffect, useState } from 'react';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';
type TimedStatusState = { status: SaveStatus; savedRevision: number };

export function useTimedStatus(resetMs = 2500) {
  const [state, setState] = useState<TimedStatusState>({ status: 'idle', savedRevision: 0 });

  const update = useCallback((next: SaveStatus) => {
    setState((current) => ({
      status: next,
      savedRevision: next === 'saved' ? current.savedRevision + 1 : current.savedRevision,
    }));
  }, []);

  useEffect(() => {
    if (state.status !== 'saved') return undefined;

    const resetTimer = setTimeout(() => {
      setState((current) => {
        if (current.status !== 'saved' || current.savedRevision !== state.savedRevision) {
          return current;
        }

        return { ...current, status: 'idle' };
      });
    }, resetMs);

    return () => clearTimeout(resetTimer);
  }, [resetMs, state.savedRevision, state.status]);

  return [state.status, update] as const;
}
