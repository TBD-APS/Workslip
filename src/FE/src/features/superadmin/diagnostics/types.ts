export type ErrorDiagnosticsRange = '1h' | '24h' | '7d';
export type ErrorDiagnosticsSource = 'all' | 'frontend' | 'backend';
export type ErrorDiagnosticsSeverity = 'error' | 'critical';

export interface ErrorDiagnosticsSummary {
  lastHour: number;
  last24Hours: number;
  last7Days: number;
  frontendLast24Hours: number;
  backendLast24Hours: number;
}

export interface ErrorDiagnosticsTelemetryHealth {
  frontendLastSeenUtc: string | null;
  backendLastSeenUtc: string | null;
}

export interface ErrorDiagnosticsItem {
  timestampUtc: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  source: 'frontend' | 'backend';
  severity: ErrorDiagnosticsSeverity;
  errorType: string;
  fingerprint: string;
  message: string;
  route: string | null;
  operation: string | null;
  release: string | null;
  correlationId: string | null;
  traceId: string | null;
  affectedReleaseCount: number;
  affectedRouteCount: number;
  affectedOperationCount: number;
  occurrences: number;
}

export interface ErrorDiagnosticsDashboard {
  isAvailable: boolean;
  isComplete: boolean;
  isStale: boolean;
  availabilityReason: string | null;
  generatedAtUtc: string;
  dataRetrievedAtUtc: string | null;
  summaryAvailable: boolean;
  itemsAvailable: boolean;
  telemetryHealthAvailable: boolean;
  hasPartialAzureResults: boolean;
  isTruncated: boolean;
  summary: ErrorDiagnosticsSummary | null;
  telemetryHealth: ErrorDiagnosticsTelemetryHealth | null;
  items: ErrorDiagnosticsItem[];
}
