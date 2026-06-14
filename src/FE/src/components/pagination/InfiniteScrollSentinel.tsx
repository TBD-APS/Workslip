interface InfiniteScrollSentinelProps {
  sentinelRef: (node: HTMLDivElement | null) => void;
  isLoading?: boolean;
}

export function InfiniteScrollSentinel({ sentinelRef, isLoading }: InfiniteScrollSentinelProps) {
  return (
    <div ref={sentinelRef} className="infinite-scroll-sentinel" aria-hidden="true">
      {isLoading && (
        <div className="infinite-scroll-loading">
          <div className="spinner" />
          <span>Indlæser flere...</span>
        </div>
      )}
    </div>
  );
}