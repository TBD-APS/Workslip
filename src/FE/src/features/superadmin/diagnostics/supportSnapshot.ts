import type {
  ErrorDiagnosticsDashboard,
  ErrorDiagnosticsRange,
  ErrorDiagnosticsSource,
} from './types';

export interface ErrorDiagnosticsSupportSnapshot {
  schemaVersion: 1;
  source: 'workslip-superadmin-error-diagnostics';
  exportedAtUtc: string;
  filters: {
    range: ErrorDiagnosticsRange;
    source: ErrorDiagnosticsSource;
  };
  dashboard: ErrorDiagnosticsDashboard;
}

export function serializeErrorDiagnosticsSupportSnapshot(
  dashboard: ErrorDiagnosticsDashboard,
  range: ErrorDiagnosticsRange,
  source: ErrorDiagnosticsSource,
  exportedAtUtc = new Date().toISOString(),
): string {
  const allowlistedDashboard: ErrorDiagnosticsDashboard = {
    isAvailable: dashboard.isAvailable,
    isComplete: dashboard.isComplete,
    isStale: dashboard.isStale,
    availabilityReason: dashboard.availabilityReason,
    generatedAtUtc: dashboard.generatedAtUtc,
    dataRetrievedAtUtc: dashboard.dataRetrievedAtUtc,
    summaryAvailable: dashboard.summaryAvailable,
    itemsAvailable: dashboard.itemsAvailable,
    telemetryHealthAvailable: dashboard.telemetryHealthAvailable,
    hasPartialAzureResults: dashboard.hasPartialAzureResults,
    isTruncated: dashboard.isTruncated,
    summary: dashboard.summary
      ? {
        lastHour: dashboard.summary.lastHour,
        last24Hours: dashboard.summary.last24Hours,
        last7Days: dashboard.summary.last7Days,
        frontendLast24Hours: dashboard.summary.frontendLast24Hours,
        backendLast24Hours: dashboard.summary.backendLast24Hours,
      }
      : null,
    telemetryHealth: dashboard.telemetryHealth
      ? {
        frontendLastSeenUtc: dashboard.telemetryHealth.frontendLastSeenUtc,
        backendLastSeenUtc: dashboard.telemetryHealth.backendLastSeenUtc,
      }
      : null,
    items: dashboard.items.map((item) => ({
      timestampUtc: item.timestampUtc,
      source: item.source,
      severity: item.severity,
      errorType: item.errorType,
      fingerprint: item.fingerprint,
      message: item.message,
      route: item.route,
      operation: item.operation,
      release: item.release,
      correlationId: item.correlationId,
      traceId: item.traceId,
      occurrences: item.occurrences,
    })),
  };

  const snapshot: ErrorDiagnosticsSupportSnapshot = {
    schemaVersion: 1,
    source: 'workslip-superadmin-error-diagnostics',
    exportedAtUtc,
    filters: { range, source },
    dashboard: allowlistedDashboard,
  };

  return JSON.stringify(snapshot, null, 2);
}
