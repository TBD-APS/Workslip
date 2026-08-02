import { useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { ClipboardCopy } from 'lucide-react';
import { useSyncExternalStore, useState } from 'react';
import { notify } from '../../../lib/toast';
import { errorDiagnosticsQueryPrefix } from './api';
import { serializeErrorDiagnosticsSupportSnapshot } from './supportSnapshot';
import type {
  ErrorDiagnosticsDashboard,
  ErrorDiagnosticsRange,
  ErrorDiagnosticsSource,
} from './types';
import './DiagnosticsSupportCopyButton.css';

function isErrorDiagnosticsRange(value: unknown): value is ErrorDiagnosticsRange {
  return value === '1h' || value === '24h' || value === '7d';
}

function isErrorDiagnosticsSource(value: unknown): value is ErrorDiagnosticsSource {
  return value === 'all' || value === 'frontend' || value === 'backend';
}

function findActiveDiagnosticsQuery(queryClient: QueryClient) {
  return queryClient
    .getQueryCache()
    .findAll({ queryKey: errorDiagnosticsQueryPrefix })
    .filter((query) => (
      query.queryKey.length === 5
      && query.getObserversCount() > 0
      && query.state.data !== undefined
    ))
    .sort((left, right) => right.state.dataUpdatedAt - left.state.dataUpdatedAt)[0];
}

function getDiagnosticsCacheVersion(queryClient: QueryClient): string {
  const query = findActiveDiagnosticsQuery(queryClient);
  return query
    ? `${query.queryHash}:${query.state.status}:${query.state.dataUpdatedAt}`
    : '';
}

export function DiagnosticsSupportCopyButton() {
  const queryClient = useQueryClient();
  const [isCopying, setIsCopying] = useState(false);

  useSyncExternalStore(
    (onStoreChange) => queryClient.getQueryCache().subscribe(onStoreChange),
    () => getDiagnosticsCacheVersion(queryClient),
    () => '',
  );

  const activeQuery = findActiveDiagnosticsQuery(queryClient);
  const queryKey = activeQuery?.queryKey;
  const range = queryKey?.[3];
  const source = queryKey?.[4];
  const dashboard = activeQuery?.state.data as ErrorDiagnosticsDashboard | undefined;
  const activeSnapshot = dashboard !== undefined
    && isErrorDiagnosticsRange(range)
    && isErrorDiagnosticsSource(source)
    ? { dashboard, range, source }
    : null;

  const handleCopy = async () => {
    if (!activeSnapshot) return;

    setIsCopying(true);
    try {
      if (!navigator.clipboard?.writeText) {
        throw new Error('clipboard_unavailable');
      }

      await navigator.clipboard.writeText(
        serializeErrorDiagnosticsSupportSnapshot(
          activeSnapshot.dashboard,
          activeSnapshot.range,
          activeSnapshot.source,
        ),
      );
      notify.success('Sanitiseret diagnostik er kopieret');
    } catch {
      notify.error('Diagnostikken kunne ikke kopieres. Prøv igen fra en sikker browserkontekst.');
    } finally {
      setIsCopying(false);
    }
  };

  return (
    <div className="diagnostics-support-copy">
      <p>
        Kopiér kun det sanitiserede dashboard-snapshot. Der sendes ikke data automatisk til ChatGPT.
      </p>
      <button
        type="button"
        className="btn btn-secondary"
        onClick={() => { void handleCopy(); }}
        disabled={!activeSnapshot || isCopying}
      >
        <ClipboardCopy size={16} aria-hidden="true" />
        <span>{isCopying ? 'Kopierer...' : 'Kopiér til ChatGPT'}</span>
      </button>
    </div>
  );
}
