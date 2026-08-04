import { useCallback, useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import {
  Activity,
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  Cpu,
  Database,
  HardDrive,
  Layers3,
  RefreshCw,
  RotateCcw,
  Server,
  ShieldCheck,
  Wifi,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { notify } from '../../../lib/toast';
import {
  cacheStatusQueryKey,
  clearCaches,
  getCacheStatus,
} from '../cacheApi';
import {
  PushRuntimeDiagnostics,
  type PushRuntimeStatus,
} from './PushRuntimeDiagnostics';
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
  pushRuntime: PushRuntimeStatus;
  storageUsage: number | null;
  storageQuota: number | null;
};

const unsupportedPushRuntime: PushRuntimeStatus = {
  supported: false,
  permission: 'unsupported',
  subscribed: false,
};

const emptyBrowserDiagnostics: BrowserDiagnostics = {
  caches: [],
  serviceWorkers: [],
  pushRuntime: unsupportedPushRuntime,
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

async function inspectPushRuntime(
  registrations: readonly ServiceWorkerRegistration[],
): Promise<PushRuntimeStatus> {
  if (!('Notification' in window) || !('PushManager' in window)) {
    return unsupportedPushRuntime;
  }

  const registration = registrations.find((candidate) => candidate.active)
    ?? registrations[0];
  const subscription = registration
    ? await registration.pushManager.getSubscription()
    : null;

  return {
    supported: true,
    permission: Notification.permission,
    subscribed: subscription !== null,
  };
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
  let registrations: ServiceWorkerRegistration[] = [];
  if ('serviceWorker' in navigator) {
    registrations = await navigator.serviceWorker.getRegistrations();
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
    pushRuntime: await inspectPushRuntime(registrations),
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

function formatTtl(seconds: number): string {
  if (seconds % 3600 === 0) return `${seconds / 3600} t`;
  if (seconds % 60 === 0) return `${seconds / 60} min`;
  return `${seconds} sek`;
}

function calculateHitRate(hits: number, misses: number): number | null {
  const requests = hits + misses;
  return requests === 0 ? null : (hits / requests) * 100;
}

function formatHitRate(hits: number, misses: number): string {
  const rate = calculateHitRate(hits, misses);
  return rate === null ? 'Ingen trafik' : `${rate.toFixed(1)} %`;
}

function getRegionDetail(type: string): string {
  return type === 'HybridCache'
    ? 'Proceslokal L1-cache med tag-baseret invalidation'
    : 'Proceslokal identitetsopslag-cache med absolut udløb';
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
        invalidations: current.invalidations + region.invalidations,
      }),
      { hits: 0, misses: 0, failures: 0, invalidations: 0 },
    );
  }, [statusQuery.data]);

  const activeQueries = frontendQueries.filter((query) => query.observers > 0).length;
  const staleQueries = frontendQueries.filter((query) => query.stale).length;
  const backendHitRate = calculateHitRate(totals.hits, totals.misses);
  const storagePercentage = browserDiagnostics.storageUsage !== null
    && browserDiagnostics.storageQuota
    ? Math.min((browserDiagnostics.storageUsage / browserDiagnostics.storageQuota) * 100, 100)
    : null;

  const health = statusQuery.isError
    ? 'critical'
    : totals.failures > 0
      ? 'warning'
      : statusQuery.isLoading
        ? 'loading'
        : 'healthy';

  const healthLabel = health === 'critical'
    ? 'Backend utilgængelig'
    : health === 'warning'
      ? 'Fejl registreret'
      : health === 'loading'
        ? 'Indlæser telemetry'
        : 'Cachelag online';

  const handleRefresh = async () => {
    await Promise.all([
      statusQuery.refetch(),
      refreshBrowserDiagnostics(),
    ]);
  };

  return (
    <div className="page-container cache-diagnostics-page">
      <header className="cache-diagnostics-hero">
        <div className="cache-diagnostics-hero-glow" aria-hidden="true" />
        <div className="cache-diagnostics-hero-topline">
          <button
            type="button"
            className="cache-diagnostics-back-button"
            onClick={() => navigate('/superadmin')}
            aria-label="Tilbage til Superadmin"
            title="Tilbage til Superadmin"
          >
            <ArrowLeft size={18} />
          </button>
          <span className="cache-diagnostics-eyebrow">
            <ShieldCheck size={14} aria-hidden="true" />
            Superadmin observability
          </span>
        </div>

        <div className="cache-diagnostics-hero-content">
          <div className="cache-diagnostics-heading">
            <h1>Cache command center</h1>
            <p>
              Live metadata fra klient, API-proces og edge-cache. Ingen payloads,
              identiteter eller komplette cache keys forlader deres sikkerhedsgrænse.
            </p>
          </div>

          <div className={`cache-diagnostics-health cache-diagnostics-health--${health}`} role="status" aria-live="polite">
            <span className="cache-diagnostics-health-pulse" aria-hidden="true" />
            <div>
              <strong>{healthLabel}</strong>
              <span>Automatisk polling hvert 15. sekund</span>
            </div>
          </div>
        </div>

        <div className="cache-diagnostics-actions">
          <button
            type="button"
            className="cache-diagnostics-button cache-diagnostics-button--secondary"
            onClick={() => { void handleRefresh(); }}
            disabled={statusQuery.isFetching || isInspectingBrowser}
          >
            <RefreshCw
              size={16}
              className={statusQuery.isFetching || isInspectingBrowser ? 'animate-spin' : undefined}
              aria-hidden="true"
            />
            Genindlæs telemetry
          </button>
          <button
            type="button"
            className="cache-diagnostics-button cache-diagnostics-button--danger"
            onClick={() => clearMutation.mutate()}
            disabled={clearMutation.isPending}
          >
            <RotateCcw size={16} aria-hidden="true" />
            {clearMutation.isPending ? 'Rydder cachelag...' : 'Ryd alle cachelag'}
          </button>
        </div>

        <div className="cache-diagnostics-layer-strip" aria-label="Observerede cachelag">
          <div>
            <HardDrive size={17} aria-hidden="true" />
            <span>Client state</span>
            <strong>React Query</strong>
          </div>
          <span className="cache-diagnostics-layer-connector" aria-hidden="true" />
          <div>
            <Cpu size={17} aria-hidden="true" />
            <span>API process</span>
            <strong>Hybrid + Memory</strong>
          </div>
          <span className="cache-diagnostics-layer-connector" aria-hidden="true" />
          <div>
            <Layers3 size={17} aria-hidden="true" />
            <span>Browser / edge</span>
            <strong>PWA + Vercel</strong>
          </div>
        </div>
      </header>

      {statusQuery.isError && (
        <div className="cache-diagnostics-alert cache-diagnostics-alert--error" role="alert">
          <AlertTriangle size={18} aria-hidden="true" />
          <div>
            <strong>Backend-telemetry kunne ikke hentes</strong>
            <span>Eksisterende cachelag fortsætter uændret. Prøv at genindlæse status.</span>
          </div>
        </div>
      )}
      {browserError && (
        <div className="cache-diagnostics-alert cache-diagnostics-alert--warning" role="alert">
          <AlertTriangle size={18} aria-hidden="true" />
          <div>
            <strong>Browsermetadata er delvist utilgængelig</strong>
            <span>{browserError}</span>
          </div>
        </div>
      )}

      <section className="cache-diagnostics-overview" aria-label="Cacheoversigt">
        <article className="cache-diagnostics-stat" data-tone="blue">
          <span className="cache-diagnostics-stat-icon"><Server size={21} aria-hidden="true" /></span>
          <div className="cache-diagnostics-stat-copy">
            <span>API-instans</span>
            <strong>{statusQuery.data?.backend.instanceId.slice(0, 8) ?? '—'}</strong>
            <small>Process startet {formatDate(statusQuery.data?.backend.startedAt)}</small>
          </div>
          <Activity size={36} className="cache-diagnostics-stat-watermark" aria-hidden="true" />
        </article>

        <article className="cache-diagnostics-stat" data-tone={totals.failures > 0 ? 'red' : 'green'}>
          <span className="cache-diagnostics-stat-icon"><Database size={21} aria-hidden="true" /></span>
          <div className="cache-diagnostics-stat-copy">
            <span>Backend hit-rate</span>
            <strong>{backendHitRate === null ? '—' : `${backendHitRate.toFixed(1)} %`}</strong>
            <small>{totals.hits} hits · {totals.misses} misses · {totals.failures} fejl</small>
          </div>
          <Database size={36} className="cache-diagnostics-stat-watermark" aria-hidden="true" />
        </article>

        <article className="cache-diagnostics-stat" data-tone={staleQueries > 0 ? 'amber' : 'violet'}>
          <span className="cache-diagnostics-stat-icon"><HardDrive size={21} aria-hidden="true" /></span>
          <div className="cache-diagnostics-stat-copy">
            <span>React Query state</span>
            <strong>{frontendQueries.length}</strong>
            <small>{activeQueries} aktive · {staleQueries} stale</small>
          </div>
          <HardDrive size={36} className="cache-diagnostics-stat-watermark" aria-hidden="true" />
        </article>

        <article className="cache-diagnostics-stat" data-tone="cyan">
          <span className="cache-diagnostics-stat-icon"><Wifi size={21} aria-hidden="true" /></span>
          <div className="cache-diagnostics-stat-copy">
            <span>Browser runtime</span>
            <strong>{browserDiagnostics.serviceWorkers.length}</strong>
            <small>{browserDiagnostics.caches.length} Cache Storage-containere</small>
          </div>
          <Wifi size={36} className="cache-diagnostics-stat-watermark" aria-hidden="true" />
        </article>
      </section>

      <section className="cache-diagnostics-section cache-diagnostics-section--backend">
        <div className="cache-diagnostics-section-header">
          <div>
            <span className="cache-diagnostics-section-eyebrow">API process telemetry</span>
            <h2>Backend cache regions</h2>
            <p>
              Proceslokale tællere fra de konkrete cache consumers. Metrics nulstilles ved App Service recycle;
              Application Insights bevarer historikken.
            </p>
          </div>
          <div className="cache-diagnostics-meta-cluster">
            <span className="cache-diagnostics-chip">
              <RefreshCw size={13} aria-hidden="true" />
              Sidst ryddet {formatDate(statusQuery.data?.backend.lastClearedAt)}
            </span>
            <span className={`cache-diagnostics-chip ${statusQuery.data?.vercelConfigured ? 'is-success' : 'is-muted'}`}>
              {statusQuery.data?.vercelConfigured
                ? <CheckCircle2 size={13} aria-hidden="true" />
                : <AlertTriangle size={13} aria-hidden="true" />}
              Vercel {statusQuery.data?.vercelConfigured ? 'konfigureret' : 'ikke konfigureret'}
            </span>
          </div>
        </div>

        <div className="cache-diagnostics-table-wrap">
          <table className="cache-diagnostics-table">
            <thead>
              <tr>
                <th>Region</th>
                <th>Cachetype</th>
                <th>TTL</th>
                <th>Hit-rate</th>
                <th>Hits</th>
                <th>Misses</th>
                <th>Sets</th>
                <th>Loads</th>
                <th>Invalid.</th>
                <th>Fejl</th>
                <th>Gns. load</th>
                <th>Senest aktiv</th>
              </tr>
            </thead>
            <tbody>
              {(statusQuery.data?.backend.regions ?? []).map((region) => (
                <tr key={region.name} className={region.failures > 0 ? 'has-errors' : undefined}>
                  <td>
                    <div className="cache-diagnostics-region-name">
                      <span className={`cache-diagnostics-region-dot ${region.failures > 0 ? 'is-error' : 'is-healthy'}`} aria-hidden="true" />
                      <div>
                        <strong>{region.name}</strong>
                        <small>{getRegionDetail(region.type)}</small>
                      </div>
                    </div>
                  </td>
                  <td><span className="cache-diagnostics-type-badge">{region.type}</span></td>
                  <td>{formatTtl(region.ttlSeconds)}</td>
                  <td><strong>{formatHitRate(region.hits, region.misses)}</strong></td>
                  <td>{region.hits}</td>
                  <td>{region.misses}</td>
                  <td>{region.sets}</td>
                  <td>{region.loads}</td>
                  <td>{region.invalidations}</td>
                  <td><span className={region.failures > 0 ? 'cache-diagnostics-error-count' : undefined}>{region.failures}</span></td>
                  <td>{formatDuration(region.averageLoadDurationMs)}</td>
                  <td>{formatDate(region.lastActivityAt)}</td>
                </tr>
              ))}
              {!statusQuery.isLoading && (statusQuery.data?.backend.regions.length ?? 0) === 0 && (
                <tr>
                  <td colSpan={12}>
                    <div className="cache-diagnostics-empty">
                      <Database size={22} aria-hidden="true" />
                      <span>Ingen cache regions er registreret i denne API-proces.</span>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section className="cache-diagnostics-section">
        <div className="cache-diagnostics-section-header">
          <div>
            <span className="cache-diagnostics-section-eyebrow">Client server-state</span>
            <h2>React Query cache</h2>
            <p>Kun sikre top-level scopes vises. Parametre, entity IDs, søgninger og cached payloads er skjult.</p>
          </div>
          <span className="cache-diagnostics-chip">
            <Activity size={13} aria-hidden="true" />
            {activeQueries} observerede queries
          </span>
        </div>

        <div className="cache-diagnostics-query-list">
          {frontendQueries.map((query) => (
            <article
              key={query.id}
              className={`cache-diagnostics-query-card ${query.stale ? 'is-stale' : 'is-fresh'} ${query.observers > 0 ? 'is-active' : ''}`}
            >
              <div className="cache-diagnostics-query-card-header">
                <strong>{query.scope}</strong>
                <span className={`cache-diagnostics-state-badge ${query.stale ? 'is-stale' : 'is-fresh'}`}>
                  {query.stale ? 'stale' : 'fresh'}
                </span>
              </div>
              <div className="cache-diagnostics-query-state">
                <span>{query.status}</span>
                <span aria-hidden="true">·</span>
                <span>{query.fetchStatus}</span>
              </div>
              <div className="cache-diagnostics-query-footer">
                <span>{query.observers} observers</span>
                <span>{formatDate(query.updatedAt)}</span>
              </div>
            </article>
          ))}
          {frontendQueries.length === 0 && (
            <div className="cache-diagnostics-empty cache-diagnostics-empty--panel">
              <HardDrive size={22} aria-hidden="true" />
              <span>Ingen React Query entries i den aktuelle session.</span>
            </div>
          )}
        </div>
      </section>

      <div className="cache-diagnostics-browser-grid">
        <section className="cache-diagnostics-section cache-diagnostics-section--compact">
          <div className="cache-diagnostics-section-header">
            <div>
              <span className="cache-diagnostics-section-eyebrow">Persistent browser layer</span>
              <h2>Cache Storage</h2>
              <p>PWA-assets og browserstyrede cachecontainere. Kun navn og entry count læses.</p>
            </div>
          </div>

          <div className="cache-diagnostics-list">
            {browserDiagnostics.caches.map((cache) => (
              <div key={cache.name}>
                <span className="cache-diagnostics-list-icon"><HardDrive size={16} aria-hidden="true" /></span>
                <div>
                  <strong>{cache.name}</strong>
                  <span>{cache.entries} entries</span>
                </div>
              </div>
            ))}
            {browserDiagnostics.caches.length === 0 && (
              <div className="cache-diagnostics-empty">
                <HardDrive size={20} aria-hidden="true" />
                <span>Ingen Cache Storage-containere.</span>
              </div>
            )}
          </div>

          <div className="cache-diagnostics-storage">
            <div>
              <span>Browser storage</span>
              <strong>{formatBytes(browserDiagnostics.storageUsage)} / {formatBytes(browserDiagnostics.storageQuota)}</strong>
            </div>
            <div className="cache-diagnostics-storage-track" aria-hidden="true">
              <span style={{ width: `${storagePercentage ?? 0}%` }} />
            </div>
          </div>
        </section>

        <section className="cache-diagnostics-section cache-diagnostics-section--compact">
          <div className="cache-diagnostics-section-header">
            <div>
              <span className="cache-diagnostics-section-eyebrow">PWA runtime</span>
              <h2>Service worker</h2>
              <p>Registrering, scope og lifecycle state. Cache-rydning afinstallerer ikke PWA-runtime.</p>
            </div>
          </div>

          <div className="cache-diagnostics-list">
            {browserDiagnostics.serviceWorkers.map((worker) => (
              <div key={worker.scope}>
                <span className="cache-diagnostics-list-icon"><Wifi size={16} aria-hidden="true" /></span>
                <div>
                  <strong>{worker.state}</strong>
                  <span>{worker.scope}</span>
                </div>
                <span className={`cache-diagnostics-state-badge ${worker.state === 'activated' ? 'is-fresh' : 'is-stale'}`}>
                  {worker.state === 'activated' ? 'online' : 'transition'}
                </span>
              </div>
            ))}
            {browserDiagnostics.serviceWorkers.length === 0 && (
              <div className="cache-diagnostics-empty">
                <Wifi size={20} aria-hidden="true" />
                <span>Ingen service worker registreret.</span>
              </div>
            )}
          </div>
        </section>

        <section className="cache-diagnostics-section cache-diagnostics-section--compact">
          <div className="cache-diagnostics-section-header">
            <div>
              <span className="cache-diagnostics-section-eyebrow">Web Push runtime</span>
              <h2>Notifikationer</h2>
              <p>Browserens permission og lokale subscription-status. Endpoint og nøgler læses eller vises ikke.</p>
            </div>
          </div>

          <PushRuntimeDiagnostics status={browserDiagnostics.pushRuntime} />
        </section>
      </div>
    </div>
  );
}
