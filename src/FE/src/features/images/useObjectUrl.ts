import { useEffect, useState } from 'react';

export function useObjectUrl(blob: Blob | undefined) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!blob) {
      setUrl(null);
      return;
    }

    const nextUrl = window.URL.createObjectURL(blob);
    setUrl(nextUrl);

    return () => {
      window.URL.revokeObjectURL(nextUrl);
    };
  }, [blob]);

  return url;
}
