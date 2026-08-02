import { useQuery } from '@tanstack/react-query';
import {
  Activity,
  AlertCircle,
  AlertTriangle,
  Clock3,
  MonitorSmartphone,
  RefreshCw,
  Server,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { getSuperadminErrorMessage } from '../api';
import { errorDiagnosticsQueryKey, getErrorDiagnostics } from './api';
import type {
  ErrorDiagnosticsItem,
  ErrorDiagnosticsRange,
  ErrorDiagnosticsSource,
} from './types';
import './ErrorDiagnosticsDashboard.css';

const rangeOptions: Array<{ value: ErrorDiagnosticsRange; label: string }> = [
  { value: '1h', label: '1 time' },
  { value: '24h', label: '24 timer' },
  { value: '7d', label: '7 dage' },
];

const sourceOptions: Array<{ value: ErrorDiagnosticsSource; label: string }> = [
  { value: 'all', label: 'Alle' },
  { value: 'frontend', label: 'Frontend' },
  { value: 'backend', label: 'Backend' },
];

function formatTimestamp(value: string): string {
  const timestamp = new Date(value);
  return Number.isNaN(timestamp.getTime())
    ? 'Ukendt tidspunkt'
    : new Intl.DateTimeFormat('da-DK', {
      dateStyle: 'short',
      timeStyle: 'medium',
    }).format(timestamp);
}

function telemetryFreshness(
  value: string | null,
  referenceUtc: string | null,
): {
  status: 'recent' | 'delayed' | 'old' | 'missing';
  timestamp: string;
  description: string;
} {
  if (!value) {
    return {
      status: 'missing',
      timestamp: 'Ikke observeret',
      description: 'Ingen telemetry er registreret i den syv-dages health-query.',
    };
  }

  const timestamp = new Date(value);
  const referenceTimestamp = referenceUtc ? new Date(referenceUtc) : new Date();
  const ageMinutes = Math.max(
    0,
    Math.floor((referenceTimestamp.getTime() - timestamp.getTime()) / 60_000),
  );
  if (ageMinutes <= 15) {
    return {
      status: 'recent',
      timestamp: formatTimestamp(value),
      description: 'Telemetry er observeret inden for de seneste 15 minutter.',
    };
  }
  if (ageMinutes <= 60) {
    return {
      status: 'delayed',
      timestamp: formatTimestamp(value),
      description: `Senest observeret for cirka ${ageMinutes} minutter siden.`,
    };
  }

  const ageHours = Math.floor(ageMinutes / 60);
  return {
    status: 'old',
    timestamp: formatTimestamp(value),
    description: `Senest observeret for cirka ${ageHours} timer siden.`,
  };
}

function singleAvailabilityMessage(reason: string): string {
  switch (reason) {
    case 'not_configured':
      return 'Application Insights-logadgang er ikke konfigureret på API’et.';
    case 'permission_denied':
      return 'API-identiteten mangler læserettighed til Log Analytics.';
    case 'throttled':
      return 'Azure begrænser logforespørgsler midlertidigt.';
    case 'timeout':
      return 'En logforespørgsel tog for lang tid.';
    case 'token_unavailable':
      return 'API’et kunne ikke hente et Azure-adgangstoken.';
    case 'invalid_response':
      return 'Azure returnerede et svar, der ikke kunne valideres sikkert.';
    case 'partial_result':
      return 'Azure oplyser, at resultatet kun er delvist.';
    default:
      return 'Logdata kunne ikke hentes fuldstændigt fra Application Insights.';
  }
}

function availabilityMessage(reason: string | null): string {
  const reasons = reason?.split(',').filter(Boolean) ?? [];
  return reasons.length === 0
    ? singleAvailabilityMessage('query_failed')
    : reasons.map(singleAvailabilityMessage).join(' ');
}

function ErrorCard({ item }: { item: ErrorDiagnosticsItem }) {
  const context = item.route ?? item.operation;

  return (
    <article className={`error-diagnostics-item severity-${item.severity}`}>
      <div className="error-diagnostics-item-topline">
        <span className={`error-source-badge source-${item.source}`}>
          {item.source === 'frontend' ? (
            <MonitorSmartphone size={14} aria-hidden="true" />
          ) : (
            <Server size={14} aria-hidden="true" />
          )}
          {item.source === 'frontend' ? 'Frontend' : 'Backend'}
        </span>
        <time dateTime={item.timestampUtc}>{formatTimestamp(item.timestampUtc)}</time>
        {item.occurrences > 1 && (
          <span className="error-occurrences" aria-label={`${item.occurrences} forekomster`}>
            ×{item.occurrences}
          </span>
        )}
      </div>

      <div className="error-diagnostics-item-heading">
        {item.severity === 'critical' ? (
          <AlertCircle size={19} aria-hidden="true" />
        ) : (
          <AlertTriangle size={19} aria-hidden="true" />
        )}
        <div>
          <h3>{item.errorType}</h3>
          <p>{item.message}</p>
        </div>
      </div>

      <dl className="error-diagnostics-metadata">
        {context && (
          <div>
            <dt>{item.route ? 'Route' : 'Operation'}</dt>
            <dd><code>{context}</code></dd>
          </div>
        )}
        {item.release && (
          <div>
            <dt>Release</dt>
            <dd><code>{item.release}</code></dd>
          </div>
        )}
        <div>
          <dt>Fingerprint</dt>
          <dd><code>{item.fingerprint}</code></dd>
        </div>
        {(item.correlationId ?? item.traceId) && (
          <div>
            <dt>{item.correlationId ? 'Correlation ID' : 'Trace ID'}</dt>
            <dd><code>{item.correlationId ?? item.traceId}</code></dd>
          </div>
        )}
      </dl>
    </article>
  );
}

export function ErrorDiagnosticsDashboard() {
  const [range, setRange] = useState<ErrorDiagnosticsRange>('24h');
  const [source, setSource] = useState<ErrorDiagnosticsSource>('all');

  const diagnosticsQuery = useQuery({
    queryKey: errorDiagnosticsQueryKey(range, source),
    queryFn: () => getErrorDiagnostics(range, source),
    staleTime: 30_000,
    gcTime: 60 * 60 * 1000,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    retry: 2,
  });

  const dashboard = diagnosticsQuery.data;
  const dataRetrievedAt = useMemo(
    () => dashboard?.dataRetrievedAtUtc ? formatTimestamp(dashboard.dataRetrievedAtUtc) : null,
    [dashboard?.dataRetrievedAtUtc],
  );
  const frontendTelemetry = telemetryFreshness(
    dashboard?.telemetryHealth?.frontendLastSeenUtc ?? null,
    dashboard?.generatedAtUtc ?? null,
  );
  const backendTelemetry = telemetryFreshness(
    dashboard?.telemetryHealth?.backendLastSeenUtc ?? null,
    dashboard?.generatedAtUtc ?? null,
  );
  const hasBackgroundRefreshError = diagnosticsQuery.isError && dashboard !== undefined;

  return (
    <div className="page-container error-diagnostics-page">
      <header className="error-diagnostics-header">
        <div className="error-diagnostics-title">
          <span className="error-diagnostics-title-icon" aria-hidden="true">
            <Activity size={27} />
          </span>
          <div>
            <p className="error-diagnostics-eyebrow">Application Insights</p>
            <h1>Fejl og driftshændelser</h1>
            <p>Sanitiserede frontend- og backendfejl. Rå logs, payloads og persondata vises ikke.</p>
          </div>
        </div>
        <button
          type="button"
          className="btn btn-secondary error-diagnostics-refresh"
          onClick={() => { void diagnosticsQuery.refetch(); }}
          disabled={diagnosticsQuery.isFetching}
        >
          <RefreshCw
            size={16}
            className={diagnosticsQuery.isFetching ? 'animate-spin' : undefined}
            aria-hidden="true"
          />
          {diagnosticsQuery.isFetching ? 'Opdaterer...' : 'Genindlæs'}
        </button>
      </header>

      <section className="error-diagnostics-controls" aria-label="Filtrér fejl">
        <fieldset>
          <legend>Tidsrum</legend>
          <div className="error-diagnostics-segmented">
            {rangeOptions.map((option) => (
              <button
                key={option.value}
                type="button"
                className={range === option.value ? 'selected' : undefined}
                aria-pressed={range === option.value}
                onClick={() => setRange(option.value)}
              >
                {option.label}
              </button>
            ))}
          </div>
        </fieldset>
        <fieldset>
          <legend>Kilde</legend>
          <div className="error-diagnostics-segmented">
            {sourceOptions.map((option) => (
              <button
                key={option.value}
                type="button"
                className={source === option.value ? 'selected' : undefined}
                aria-pressed={source === option.value}
                onClick={() => setSource(option.value)}
              >
                {option.label}
              </button>
            ))}
          </div>
        </fieldset>
        <div className="error-diagnostics-generated" aria-live="polite">
          <Clock3 size={15} aria-hidden="true" />
          {dataRetrievedAt
            ? `${dashboard?.isStale ? 'Sidst bekræftet' : 'Hentet'} ${dataRetrievedAt}`
            : 'Afventer logdata'}
        </div>
      </section>

      {hasBackgroundRefreshError && (
        <div className="error-diagnostics-state warning" role="status">
          <AlertTriangle size={22} aria-hidden="true" />
          <div>
            <strong>Seneste opdatering mislykkedes</strong>
            <span>Det sidst validerede datasæt vises fortsat.</span>
          </div>
        </div>
      )}

      {dashboard?.isStale && (
        <div className="error-diagnostics-state warning" role="status">
          <AlertTriangle size={22} aria-hidden="true" />
          <div>
            <strong>Viser sidste kendte komplette snapshot</strong>
            <span>{availabilityMessage(dashboard.availabilityReason)} Tallene er ikke aktuelle.</span>
          </div>
        </div>
      )}

      {dashboard && !dashboard.isStale && !dashboard.isComplete && dashboard.isAvailable && (
        <div className="error-diagnostics-state warning" role="status">
          <AlertTriangle size={22} aria-hidden="true" />
          <div>
            <strong>Resultatet er ikke komplet</strong>
            <span>{availabilityMessage(dashboard.availabilityReason)} Manglende sektioner vises ikke som nul.</span>
          </div>
        </div>
      )}

      {dashboard?.isTruncated && (
        <div className="error-diagnostics-state warning" role="status">
          <AlertTriangle size={22} aria-hidden="true" />
          <div>
            <strong>Listen er afkortet</strong>
            <span>Oversigtstallene er komplette, men listen viser kun de seneste fejlgrupper.</span>
          </div>
        </div>
      )}

      {diagnosticsQuery.isLoading && !dashboard ? (
        <div className="error-diagnostics-state" role="status">
          <RefreshCw className="animate-spin" size={22} aria-hidden="true" />
          Henter fejl fra Application Insights...
        </div>
      ) : diagnosticsQuery.isError && !dashboard ? (
        <div className="error-diagnostics-state error" role="alert">
          <AlertCircle size={22} aria-hidden="true" />
          <div>
            <strong>Fejlene kunne ikke hentes</strong>
            <span>{getSuperadminErrorMessage(diagnosticsQuery.error)}</span>
          </div>
          <button type="button" className="btn btn-secondary" onClick={() => { void diagnosticsQuery.refetch(); }}>
            Prøv igen
          </button>
        </div>
      ) : dashboard && !dashboard.isAvailable ? (
        <div className="error-diagnostics-state warning" role="status">
          <AlertTriangle size={22} aria-hidden="true" />
          <div>
            <strong>Logdashboardet er utilgængeligt</strong>
            <span>{availabilityMessage(dashboard.availabilityReason)} Der vises ikke falske nuller.</span>
          </div>
          <button type="button" className="btn btn-secondary" onClick={() => { void diagnosticsQuery.refetch(); }}>
            Prøv igen
          </button>
        </div>
      ) : dashboard ? (
        <>
          {dashboard.telemetryHealthAvailable && dashboard.telemetryHealth ? (
            <section className="error-diagnostics-telemetry-health" aria-labelledby="telemetry-health-title">
              <div className="error-diagnostics-list-heading">
                <div>
                  <h2 id="telemetry-health-title">Telemetry senest observeret</h2>
                  <p>Et nul i fejltallene er kun troværdigt, når telemetry-pipelinen også kan bekræftes.</p>
                </div>
              </div>
              <div className="error-diagnostics-health-grid">
                <article className={`error-diagnostics-health-card ${frontendTelemetry.status}`}>
                  <MonitorSmartphone size={20} aria-hidden="true" />
                  <div>
                    <span>Frontend heartbeat</span>
                    {dashboard.telemetryHealth.frontendLastSeenUtc ? (
                      <time dateTime={dashboard.telemetryHealth.frontendLastSeenUtc}>
                        {frontendTelemetry.timestamp}
                      </time>
                    ) : (
                      <strong>{frontendTelemetry.timestamp}</strong>
                    )}
                    <small>{frontendTelemetry.description}</small>
                  </div>
                </article>
                <article className={`error-diagnostics-health-card ${backendTelemetry.status}`}>
                  <Server size={20} aria-hidden="true" />
                  <div>
                    <span>Backend requests</span>
                    {dashboard.telemetryHealth.backendLastSeenUtc ? (
                      <time dateTime={dashboard.telemetryHealth.backendLastSeenUtc}>
                        {backendTelemetry.timestamp}
                      </time>
                    ) : (
                      <strong>{backendTelemetry.timestamp}</strong>
                    )}
                    <small>{backendTelemetry.description}</small>
                  </div>
                </article>
              </div>
            </section>
          ) : (
            <div className="error-diagnostics-state warning" role="status">
              <AlertTriangle size={22} aria-hidden="true" />
              <div>
                <strong>Telemetry-pipelinen kunne ikke bekræftes</strong>
                <span>Fejltallene må ikke læses som bevis på fejlfri drift, før health-queryen virker.</span>
              </div>
            </div>
          )}

          {dashboard.summaryAvailable && dashboard.summary ? (
            <section className="error-diagnostics-summary" aria-label="Fejloversigt">
              <article>
                <span>Seneste time</span>
                <strong>{dashboard.summary.lastHour}</strong>
              </article>
              <article>
                <span>Seneste 24 timer</span>
                <strong>{dashboard.summary.last24Hours}</strong>
              </article>
              <article>
                <span>Seneste 7 dage</span>
                <strong>{dashboard.summary.last7Days}</strong>
              </article>
              <article className="error-diagnostics-source-summary">
                <span>Fordeling · 24 timer</span>
                <div>
                  <strong>{dashboard.summary.frontendLast24Hours}</strong>
                  <small>Frontend</small>
                </div>
                <div>
                  <strong>{dashboard.summary.backendLast24Hours}</strong>
                  <small>Backend</small>
                </div>
              </article>
            </section>
          ) : (
            <div className="error-diagnostics-state warning" role="status">
              <AlertTriangle size={22} aria-hidden="true" />
              <div>
                <strong>Oversigtstal er ikke tilgængelige</strong>
                <span>Der vises ingen nuller, før oversigtsqueryen er valideret.</span>
              </div>
            </div>
          )}

          <section className="error-diagnostics-list" aria-labelledby="error-list-title">
            <div className="error-diagnostics-list-heading">
              <div>
                <h2 id="error-list-title">Seneste grupperede fejl</h2>
                <p>Listen følger valgt kilde og tidsrum. Ens hændelser grupperes via et sanitiseret fingerprint.</p>
              </div>
              {dashboard.itemsAvailable && <span>{dashboard.items.length} grupper</span>}
            </div>

            {!dashboard.itemsAvailable ? (
              <div className="error-diagnostics-state warning" role="status">
                <AlertTriangle size={22} aria-hidden="true" />
                <div>
                  <strong>Fejllisten er ikke tilgængelig</strong>
                  <span>Der vises ingen tom liste, før detaljequeryen er valideret.</span>
                </div>
              </div>
            ) : dashboard.items.length === 0 ? (
              <div className="error-diagnostics-state empty" role="status">
                <Activity size={22} aria-hidden="true" />
                Ingen fejl matcher de valgte filtre.
              </div>
            ) : (
              <div className="error-diagnostics-items">
                {dashboard.items.map((item) => (
                  <ErrorCard key={`${item.fingerprint}-${item.timestampUtc}`} item={item} />
                ))}
              </div>
            )}
          </section>
        </>
      ) : null}
    </div>
  );
}
