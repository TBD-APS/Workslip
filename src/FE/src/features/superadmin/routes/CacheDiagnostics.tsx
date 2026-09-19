import { useCallback, useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import {
  Activity,
  AlertTriangle,
  ArrowLeft,
  Cpu,
  Database,
  HardDrive,
  Layers3,
  Network,
  RefreshCw,
  RotateCcw,
  Server,
  ShieldCheck,
  Wifi,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import {
  CacheClearScope,
  CacheTier,
  DistributedCacheState,
  type DistributedCacheSnapshot,
} from '../../../api/generated/models';
import { notify } from '../../../lib/toast';
import {
  cacheStatusQueryKey,
  clearCaches,
  describeDistributedFailure,
  getCacheStatus,
  type CacheClearResponse,
  type CacheStatusResponse,
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

function getRegionDetail(tier: CacheTier): string {
  return tier === CacheTier.LocalAndDistributed
    ? 'Proceslokal L1 foran delt L2 — hver replika har fortsat sin egen L1'
    : 'Kun proceslokal — hver replika har sin egen kopi';
}

function formatTier(tier: CacheTier): string {
  return tier === CacheTier.LocalAndDistributed ? 'L1 + L2' : 'Kun L1';
}

function formatClearScope(scope: CacheClearScope): string {
  return scope === CacheClearScope.ProcessAndDistributedTier
    ? 'Denne proces + delt L2'
    : 'Kun denne proces';
}

function describeDistributedTier(distributed: DistributedCacheSnapshot | undefined): string {
  if (!distributed || distributed.state === DistributedCacheState.NotConfigured) {
    return 'Ikke konfigureret';
  }
  return distributed.state === DistributedCacheState.Unreachable
    ? 'Utilgængelig'
    : distributed.provider ?? 'Tilsluttet';
}

/**
 * En rydning rammer den proces, der besvarer kaldet, og — hvis en delt cache er
 * konfigureret *og* svarer — det delte niveau. Den rammer aldrig de øvrige
 * replikaers proceslokale L1, så skærmen må ikke fremstille den som en global
 * rydning. `clearScope` følger nu tilgængelighed og ikke konfiguration, så en
 * registreret men død delt cache giver `ProcessOnly`; her siges det højt, så
 * operatøren kan se hvorfor rækkevidden er smallere end topologien.
 */
function describeClearReach(status: CacheStatusResponse | undefined): string {
  if (status?.clearReachesEveryReplica) {
    return 'Rydning rammer alle replikaer';
  }

  if (status?.clearScope === CacheClearScope.ProcessAndDistributedTier) {
    // WidestClearScope is the widest scope any single region gets, not the scope
    // of the clear as a whole — the per-region column below is authoritative.
    return 'Rydning rammer højst denne API-proces og det delte niveau (se pr. region)';
  }

  return status?.distributed.state === DistributedCacheState.Unreachable
    ? 'Rydning rammer kun denne API-proces — det delte niveau svarer ikke'
    : 'Rydning rammer kun denne API-proces';
}

function describeClearOutcome(result: CacheClearResponse): string {
  const instance = result.instanceId.slice(0, 8);

  if (result.reachedEveryReplica) {
    return `Cachelagene er ryddet i hele deployment. Kaldet blev besvaret af API-instans ${instance}.`;
  }

  if (!result.distributed.configured) {
    return `Cachelagene i API-instans ${instance} er ryddet. Der er ingen delt cache`
      + ' konfigureret, så hver anden replika beholder sin egen kopi, indtil den udløber.';
  }

  if (!result.distributedTierCleared) {
    return `Cachelagene i API-instans ${instance} er ryddet, men det delte niveau kunne`
      + ' ikke markeres som ugyldigt og leverer fortsat sit gemte indhold.';
  }

  // Tidligere sluttede sætningen "indtil de udløber eller replikaen genstarter",
  // hvilket antydede en konvergens, der ikke sker: markeringen sletter ikke de
  // delte payloads, og en replika, der allerede har læst tagget, har husket
  // tidsstemplet for hele processens levetid — så den genindlæser den gamle
  // payload, når dens egen kopi udløber. Kun en genstart konvergerer den.
  return `Cachelagene i API-instans ${instance} er ryddet, og det delte niveau er markeret`
    + ' som ugyldigt, så processer, der starter herefter, kasserer det. Markeringen sletter'
    + ' ikke de delte payloads, så en replika, der allerede kører, leverer fortsat sin egen'
    + ' kopi og kan genindlæse den delte, når kopien udløber. Kun en genstart konvergerer den.';
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

      // Rydningen dækker denne browser og den API-proces, der besvarede kaldet.
      // Beskeden må ikke antyde mere end det — se panelet under handlingerne.
      if (result.distributed.configured && !result.distributedTierCleared) {
        notify.warning('Denne browser og API-instansen er ryddet, men det delte niveau blev ikke markeret.');
      } else {
        notify.success('Denne browser og den betjenende API-instans er ryddet.');
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

  const distributed = statusQuery.data?.distributed;
  const distributedUnreachable = distributed?.state === DistributedCacheState.Unreachable;
  // Aldrig `distributed.error` direkte: feltet er et lukket vokabular fra backend,
  // og skærmen må hverken vise engelsk driftstekst eller — hvis en fremtidig
  // ændring skulle slippe provider-tekst igennem — cachens adresse.
  const distributedFailure = describeDistributedFailure(distributed?.error);

  const health = statusQuery.isError
    ? 'critical'
    : totals.failures > 0 || distributedUnreachable
      ? 'warning'
      : statusQuery.isLoading
        ? 'loading'
        : 'healthy';

  const healthLabel = health === 'critical'
    ? 'Backend utilgængelig'
    : health === 'warning'
      ? distributedUnreachable ? 'Delt cache utilgængelig' : 'Fejl registreret'
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
              Live metadata fra klient, API-proces, det delte cacheniveau og browserens
              cachelag. Ingen payloads, identiteter eller komplette cache keys forlader
              deres sikkerhedsgrænse.
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
            {clearMutation.isPending ? 'Rydder cachelag...' : 'Ryd cachelag'}
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
            <span>API process (L1)</span>
            <strong>Proceslokal</strong>
          </div>
          <span className="cache-diagnostics-layer-connector" aria-hidden="true" />
          <div>
            <Network size={17} aria-hidden="true" />
            <span>Delt cache (L2)</span>
            <strong>{describeDistributedTier(distributed)}</strong>
          </div>
          <span className="cache-diagnostics-layer-connector" aria-hidden="true" />
          <div>
            <Layers3 size={17} aria-hidden="true" />
            <span>Browser / PWA</span>
            <strong>Service worker + Cache Storage</strong>
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
      {distributedUnreachable && (
        <div className="cache-diagnostics-alert cache-diagnostics-alert--warning" role="alert">
          <AlertTriangle size={18} aria-hidden="true" />
          <div>
            <strong>Det delte cacheniveau svarer ikke</strong>
            <span>
              {distributed?.provider ?? 'Den delte cache'} er konfigureret, men svarede ikke
              på et opslag. API'en kører videre på sit proceslokale niveau og henter data fra
              kilden. En rydning rammer kun denne API-proces, indtil niveauet svarer igen.
              {distributedFailure ? ` Årsag: ${distributedFailure}` : ''}
              {distributed?.checkedAt ? ` Kontrolleret ${formatDate(distributed.checkedAt)}.` : ''}
            </span>
          </div>
        </div>
      )}
      {clearMutation.data && (
        <div
          className={`cache-diagnostics-alert ${
            clearMutation.data.reachedEveryReplica ? '' : 'cache-diagnostics-alert--warning'
          }`}
          role="status"
        >
          <AlertTriangle size={18} aria-hidden="true" />
          <div>
            <strong>
              {clearMutation.data.reachedEveryReplica
                ? 'Rydningen dækkede hele deployment'
                : 'Rydningen dækkede ikke hele deployment'}
            </strong>
            <span>{describeClearOutcome(clearMutation.data)}</span>
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
              Proceslokale tællere fra de konkrete cache consumers. Metrics nulstilles, når
              API-processen genstarter; Application Insights bevarer historikken.
            </p>
          </div>
          <div className="cache-diagnostics-meta-cluster">
            <span className="cache-diagnostics-chip">
              <AlertTriangle size={13} aria-hidden="true" />
              {describeClearReach(statusQuery.data)}
            </span>
            <span className="cache-diagnostics-chip">
              <RefreshCw size={13} aria-hidden="true" />
              Sidst ryddet {formatDate(statusQuery.data?.backend.lastClearedAt)}
            </span>
          </div>
        </div>

        <div className="cache-diagnostics-table-wrap">
          <table className="cache-diagnostics-table">
            <thead>
              <tr>
                <th>Region</th>
                <th>Cachetype</th>
                <th>Rydning rammer</th>
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
                        <small title={getRegionDetail(region.tier)}>{getRegionDetail(region.tier)}</small>
                      </div>
                    </div>
                  </td>
                  <td>
                    <span className="cache-diagnostics-type-badge">{region.type}</span>
                    {' '}
                    <span className="cache-diagnostics-type-badge">{formatTier(region.tier)}</span>
                  </td>
                  <td>
                    <span
                      className={`cache-diagnostics-state-badge ${
                        region.clearScope === CacheClearScope.ProcessAndDistributedTier ? 'is-fresh' : 'is-stale'
                      }`}
                    >
                      {formatClearScope(region.clearScope)}
                    </span>
                  </td>
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
                  <td colSpan={13}>
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
