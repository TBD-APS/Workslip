import { ChevronLeft, ChevronRight, Loader2 } from 'lucide-react';

interface PaginationControlsProps {
  page: number;
  totalCount: number;
  pageSize: number;
  onPrev: () => void;
  onNext: () => void;
  /** @deprecated Use onNext with totalCount from BE instead */
  hasNextPage?: boolean;
  /** @deprecated Use totalCount instead */
  isFetchingNextPage?: boolean;
  /** @deprecated No longer needed when totalCount is accurate */
  onLoadMore?: () => void;
}

export function PaginationControls({
  page,
  totalCount,
  pageSize,
  onPrev,
  onNext,
  hasNextPage,
  isFetchingNextPage,
  onLoadMore,
}: PaginationControlsProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const isLastLoadedPage = page >= totalPages;

  return (
    <div className="pagination-controls">
      <button
        type="button"
        className="btn btn-secondary"
        disabled={page <= 1}
        onClick={onPrev}
      >
        <ChevronLeft size={16} /> Forrige
      </button>

      <span className="pagination-info">
        {isLastLoadedPage && hasNextPage
          ? `Side ${page}`
          : `Side ${Math.min(page, totalPages)} af ${totalPages}`}
      </span>

      {isLastLoadedPage && hasNextPage ? (
        <button
          type="button"
          className="btn btn-secondary"
          disabled={isFetchingNextPage}
          onClick={onLoadMore}
        >
          {isFetchingNextPage ? (
            <><Loader2 size={16} className="spinner" /> Henter...</>
          ) : (
            <>Hent flere <ChevronRight size={16} /></>
          )}
        </button>
      ) : (
        <button
          type="button"
          className="btn btn-secondary"
          disabled={page >= totalPages}
          onClick={onNext}
        >
          Næste <ChevronRight size={16} />
        </button>
      )}
    </div>
  );
}
