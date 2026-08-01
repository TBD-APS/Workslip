import { useCallback, useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { ArrowLeft, Database, HardDrive, RefreshCw, RotateCcw, Server, Wifi } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { notify } from '../../../lib/toast';
import {
  cacheStatusQueryKey,
  clearCaches,
  getCacheStatus,
} from '../cacheApi';
import './CacheDiagnostics.css';

type FrontendQueryInfo = {
  id: string;
  scope: string;
  status: string;
  fetchStatus: string;
  stale: boolean;
  observers: number;
  updatedAt: number | null;
};

type BrowserCacheInfo = {
  name: string;
  entries: number;
};

type ServiceWorkerInfo = {
  scope: string;
  state: string;
};

type BrowserDiagnostics = {
  caches: BrowserCacheInfo[];
  serviceWorkers: ServiceWorkerInfo[];
  storageUsage: number | null;
  storageQuota: number | null;
};

const emptyBrowserDiagnostics: BrowserDiagnostics = {
  caches: [],
  serviceWorkers: [],
  storageUsage: null,
  storageQuota: null,
};

function getSafeQueryScope(queryKey: readonly unknown[]): string {
  const first = queryKey[0];
  return typeof first === 'string' && first.trim() ? first : 'query';
}

function collectFrontendQueries(queryClient: QueryClient): FrontendQueryInfo[] {
  return queryClient.getQueryCache().getAll().map((query, index) => ({
    id: `${query.queryHash}-${index}`,
    scope: getSafeQueryScope(query.queryKey),
    status: query.state.status,
    fetchStatus: query.state.fetchStatus,
    stale: query.isStale(),
    observers: query.getObserversCount(),
    updatedAt: query.state.dataUpdatedAt || null,
  }));
}

async function inspectBrowserDiagnostics(): Promise<BrowserDiagnostics> {
  const browserCaches: BrowserCacheInfo[] = [];

  if ('caches' in window) {
    const names = await window.caches.keys();
    for (const name of names) {
      const cache = await window.caches.open(name);
      const entries = await cache.keys();
      browserCaches.push({ name, entries: entries.length });
    }
  }

  const serviceWorkers: ServiceWorkerInfo[] = [];
  if ('serviceWorker' in navigator) {
    const registrations = await navigator.serviceWorker.getRegistrations();
    registrations.forEach((registration) => {
      serviceWorkers.push({
        scope: registration.scope,
        state: registration.active?.state
          ?? registration.waiting?.state
          ?? registration.installing?.state
          ?? 'inactive',
      });
    });
  }

  const estimate = navigator.storage?.estimate
    ? await navigator.storage.estimate()
    : undefined;

  return {
    caches: browserCaches.sort((left, right) => left.name.localeCompare(right.name)),
    serviceWorkers,
    storageUsage: estimate?.usage ?? null,
    storageQuota: estimate?.quota ?? null,
  };
}

function formatDate(value: string | number | null | undefined): string {
  if (!value) return 'Ingen data';
  return new Intl.DateTimeFormat('da-DK', {
    dateStyle: 'short',
    timeStyle: 'medium',
  }).format(new Date(value));
}

function formatDuration(milliseconds: number): string {
  return `${milliseconds.toFixed(milliseconds >= 10 ? 0 : 1)} ms`;
}

function formatBytes(value: number | null): string {
  if (value === null) return 'Ikke understøttet';
  return new Intl.NumberFormat('da-DK', {
    style: 'unit',
    unit: 'megabyte',
    maximumFractionDigits: 1,
  }).format(value / 1024 / 1024);
}

function isCacheStatusQuery(queryKey: readonly unknown[]): boolean {
  return queryKey.length >= 2
    && queryKey[0] === cacheStatusQueryKey[0]
    && queryKey[1] === cacheStatusQueryKey[1];
}

export function CacheDiagnostics() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [frontendQueries, setFrontendQueries] = useState<FrontendQueryInfo[]>(() => collectFrontendQueries(queryClient));
  const [browserDiagnostics, setBrowserDiagnostics] = useState<BrowserDiagnostics>(emptyBrowserDiagnostics);
  const [browserError, setBrowserError] = useState<string | null>(null);
  const [isInspectingBrowser, setIsInspectingBrowser] = useState(false);

  const statusQuery = useQuery({
    queryKey: cacheStatusQueryKey,
    queryFn: getCacheStatus,
    refetchInterval: 15_000,
  });

  const refreshBrowserDiagnostics = useCallback(async () => {
    setIsInspectingBrowser(true);
    setBrowserError(null);
    try {
      setBrowserDiagnostics(await inspectBrowserDiagnostics());
    } catch {
      setBrowserError('Browserens cachemetadata kunne ikke læses.');
    } finally {
      setIsInspectingBrowser(false);
    }
  }, []);

  useEffect(() => {
    const cache = queryClient.getQueryCache();
    const update = () => setFrontendQueries(collectFrontendQueries(queryClient));
    update();
    return cache.subscribe(update);
  }, [queryClient]);

  useEffect(() => {
    void refreshBrowserDiagnostics();
  }, [refreshBrowserDiagnostics]);

  const clearMutation = useMutation({
    mutationFn: clearCaches,
    onSuccess: async (result) => {
      queryClient.removeQueries({
        predicate: (query) => !isCacheStatusQuery(query.queryKey),
      });

      if ('caches' in window) {
        const names = await window.caches.keys();
        await Promise.all(names.map((name) => window.caches.delete(name)));
      }

      if (result.warning) {
        notify.warning('Lokale caches blev ryddet, men Vercel kunne ikke ryddes.');
      } else {
        notify.success('Frontend- og backendcaches er ryddet.');
      }

      await Promise.all([
        statusQuery.refetch(),
        refreshBrowserDiagnostics(),
      ]);
    },
  });

  const totals = useMemo(() => {
    const regions = statusQuery.data?.backend.regions ?? [];
    return regions.reduce(
      (current, region) => ({
        hits: current.hits + region.hits,
        misses: current.misses + region.misses,
        failures: current.failures + region.failures,
      }),
      { hits: 0, misses: 0, failures: 0 },
    );
  }, [statusQuery.data]);

  const handleRefresh = async () => {
    await Promise.all([
      statusQuery.refetch(),
      refreshBrowserDiagnostics(),
    ]);
  };

  return (
    <div className="page-container cache-diagnostics-page">
      <header className="cache-diagnostics-header">
        <div className="cache-diagnostics-heading">
          <button
            type="button"
            className="user-avatar"
            onClick={() => navigate('/superadmin')}
            aria-label="Tilbage til Superadmin"
            title="Tilbage"
          >
            <ArrowLeft size={18} />
          </button>
          <div>
            <h1>Cache-diagnostik</h1>
            <p>Metadata og tællere uden cachede værdier, persondata eller komplette cache keys.</p>
          </div>
        </div>
        <div className="cache-diagnostics-actions">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => { void handleRefresh(); }}
            disabled={statusQuery.isFetching || isInspectingBrowser}
          >
            <RefreshCw size={16} className={statusQuery.isFetching || isInspectingBrowser ? 'animate-spin' : undefined} />
            Genindlæs
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => clearMutation.mutate()}
            disabled={clearMutation.isPending}
          >
            <RotateCcw size={16} />
            {clearMutation.isPending ? 'Rydder...' : 'Ryd alle caches'}
          </button>
        </div>
      </header>

      {statusQuery.isError && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          Backendens cache-status kunne ikke hentes.
        </div>
      )}
      {browserError && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          {browserError}
        </div>
      )}

      <section className="cache-diagnostics-overview" aria-label="Cacheoversigt">
        <article>
          <Server size={20} />
          <span>Backend-instans</span>
          <strong>{statusQuery.data?.backend.instanceId.slice(0, 8) ?? '—'}</strong>
          <small>Startet {formatDate(statusQuery.data?.backend.startedAt)}</small>
        </article>
        <article>
          <Database size={20} />
          <span>Backend hits / misses</span>
          <strong>{totals.hits} / {totals.misses}</strong>
          <small>Fejl: {totals.failures}</small>
        </article>
        <article>
          <HardDrive size={20} />
          <span>React Query</span>
          <strong>{frontendQueries.length}</strong>
          <small>{frontendQueries.filter((query) => query.observers > 0).length} aktive queries</small>
        </article>
        <article>
          <Wifi size={20} />
          <span>Service workers</span>
          <strong>{browserDiagnostics.serviceWorkers.length}</strong>
          <small>Cache Storage: {browserDiagnostics.caches.length}</small>
        </article>
      </section>

      <section className="cache-diagnostics-section">
        <div className="cache-diagnostics-section-header">
          <div>
            <h2>Backend-cache</h2>
            <p>Proceslokale tællere. De nulstilles, når API-instansen genstarter.</p>
          </div>
          <span className="cache-diagnostics-meta">
            Sidst ryddet: {formatDate(statusQuery.data?.backend.lastClearedAt)} · Vercel: {statusQuery.data?.vercelConfigured ? 'konfigureret' : 'ikke konfigureret'}
          </span>
        </div>

        <div className="cache-diagnostics-table-wrap">
          <table className="cache-diagnostics-table">
            <thead>
              <tr>
                <th>Område</th>
                <th>Type</th>
                <th>TTL</th>
                <th>Hits</th>
                <th>Misses</th>
                <th>Sets</th>
                <th>Invalid.</th>
                <th>Fejl</th>
                <th>Load</th>
                <th>Senest aktiv</th>
              </tr>
            </thead>
            <tbody>
              {(statusQuery.data?.backend.regions ?? []).map((region) => (
                <tr key={region.name}>
                  <td><strong>{region.name}</strong></td>
                  <td>{region.type}</td>
                  <td>{region.ttlSeconds}s</td>
                  <td>{region.hits}</td>
                  <td>{region.misses}</td>
                  <td>{region.sets}</td>
                  <td>{region.invalidations}</td>
                  <td>{region.failures}</td>
                  <td>{formatDuration(region.averageLoadDurationMs)}</td>
                  <td>{formatDate(region.lastActivityAt)}</td>
                </tr>
              ))}
              {!statusQuery.isLoading && (statusQuery.data?.backend.regions.length ?? 0) === 0 && (
                <tr>
                  <td colSpan={10}>Ingen cacheområder er registreret.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section className="cache-diagnostics-section">
        <div className="cache-diagnostics-section-header">
          <div>
            <h2>Frontend server-state</h2>
            <p>Kun queryens sikre topniveau vises. Parametre, IDs og data er skjult.</p>
          </div>
        </div>
        <div className="cache-diagnostics-query-list">
          {frontendQueries.map((query) => (
            <article key={query.id}>
              <strong>{query.scope}</strong>
              <span>{query.status} · {query.fetchStatus}</span>
              <span>{query.stale ? 'stale' : 'fresh'} · {query.observers} observers</span>
              <small>Opdateret: {formatDate(query.updatedAt)}</small>
            </article>
          ))}
          {frontendQueries.length === 0 && (
            <div className="superadmin-empty">Ingen React Query entries.</div>
          )}
        </div>
      </section>

      <div className="cache-diagnostics-browser-grid">
        <section className="cache-diagnostics-section">
          <div className="cache-diagnostics-section-header">
            <div>
              <h2>Cache Storage</h2>
              <p>Browserens PWA- og asset-caches.</p>
            </div>
          </div>
          <div className="cache-diagnostics-list">
            {browserDiagnostics.caches.map((cache) => (
              <div key={cache.name}>
                <strong>{cache.name}</strong>
                <span>{cache.entries} entries</span>
              </div>
            ))}
            {browserDiagnostics.caches.length === 0 && <span>Ingen Cache Storage entries.</span>}
          </div>
          <p className="cache-diagnostics-storage">
            Lager: {formatBytes(browserDiagnostics.storageUsage)} / {formatBytes(browserDiagnostics.storageQuota)}
          </p>
        </section>

        <section className="cache-diagnostics-section">
          <div className="cache-diagnostics-section-header">
            <div>
              <h2>Service worker</h2>
              <p>Registrering og aktiv tilstand. Rydning afinstallerer ikke PWA'en.</p>
            </div>
          </div>
          <div className="cache-diagnostics-list">
            {browserDiagnostics.serviceWorkers.map((worker) => (
              <div key={worker.scope}>
                <strong>{worker.state}</strong>
                <span>{worker.scope}</span>
              </div>
            ))}
            {browserDiagnostics.serviceWorkers.length === 0 && <span>Ingen service worker registreret.</span>}
          </div>
        </section>
      </div>
    </div>
  );
}
