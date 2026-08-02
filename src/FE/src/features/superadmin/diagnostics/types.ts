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

export interface ErrorDiagnosticsItem {
  timestampUtc: string;
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
  occurrences: number;
}

export interface ErrorDiagnosticsDashboard {
  isAvailable: boolean;
  availabilityReason: string | null;
  generatedAtUtc: string;
  summary: ErrorDiagnosticsSummary;
  items: ErrorDiagnosticsItem[];
}
