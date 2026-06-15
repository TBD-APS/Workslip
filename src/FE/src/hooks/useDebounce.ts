import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Debounced value hook - returns a debounced version of the input value.
 * The debounced value updates after the specified delay without new changes.
 */
export function useDebounce<T>(value: T, delayMs: number): T {
  const [debouncedValue, setDebouncedValue] = useState(value);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const updateValue = useCallback(
    (newValue: T) => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }

      timerRef.current = setTimeout(() => {
        setDebouncedValue(newValue);
      }, delayMs);
    },
    [delayMs]
  );

  useEffect(() => {
    updateValue(value);
    return () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }
    };
  }, [value, updateValue]);

  return debouncedValue;
}
