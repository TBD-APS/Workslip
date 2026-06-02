import { useCallback, useEffect, useRef, useState } from 'react';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export function useTimedStatus(resetMs = 2500) {
  const [status, setStatus] = useState<SaveStatus>('idle');
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const update = useCallback(
    (next: SaveStatus) => {
      setStatus(next);
      if (next === 'saved') {
        clearTimeout(timerRef.current);
        timerRef.current = setTimeout(() => setStatus('idle'), resetMs);
      }
    },
    [resetMs],
  );

  useEffect(() => {
    return () => clearTimeout(timerRef.current);
  }, []);

  return [status, update] as const;
}
