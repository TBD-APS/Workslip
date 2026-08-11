import { useEffect, useMemo } from 'react';

export function useObjectUrl(blob: Blob | undefined) {
  const url = useMemo(
    () => (blob ? window.URL.createObjectURL(blob) : null),
    [blob],
  );

  useEffect(() => () => {
    if (url) {
      window.URL.revokeObjectURL(url);
    }
  }, [url]);

  return url;
}
