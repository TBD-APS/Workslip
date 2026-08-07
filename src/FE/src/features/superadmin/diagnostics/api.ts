import { apiClient } from '../../../lib/axios';
import type {
  ErrorDiagnosticsDashboard,
  ErrorDiagnosticsItem,
  ErrorDiagnosticsRange,
  ErrorDiagnosticsSource,
  ErrorDiagnosticsSummary,
  ErrorDiagnosticsTelemetryHealth,
} from './types';

export { errorDiagnosticsQueryKey, errorDiagnosticsQueryPrefix } from './queryKeys';

const invalidResponseMessage = 'Logdashboardet modtog et ugyldigt svar fra API’et.';

type JsonRecord = Record<string, unknown>;

function requireRecord(value: unknown): JsonRecord {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(invalidResponseMessage);
  }
  return value as JsonRecord;
}

function requireBoolean(record: JsonRecord, key: string): boolean {
  const value = record[key];
  if (typeof value !== 'boolean') throw new Error(invalidResponseMessage);
  return value;
}

function requireString(record: JsonRecord, key: string): string {
  const value = record[key];
  if (typeof value !== 'string' || value.length === 0) throw new Error(invalidResponseMessage);
  return value;
}

function nullableString(record: JsonRecord, key: string): string | null {
  const value = record[key];
  if (value === null) return null;
  if (typeof value !== 'string') throw new Error(invalidResponseMessage);
  return value;
}

function requireTimestamp(record: JsonRecord, key: string): string {
  const value = requireString(record, key);
  if (Number.isNaN(Date.parse(value))) throw new Error(invalidResponseMessage);
  return value;
}

function nullableTimestamp(record: JsonRecord, key: string): string | null {
  const value = nullableString(record, key);
  if (value !== null && Number.isNaN(Date.parse(value))) throw new Error(invalidResponseMessage);
  return value;
}

function requireCount(record: JsonRecord, key: string, allowZero = true): number {
  const value = record[key];
  if (
    typeof value !== 'number'
    || !Number.isSafeInteger(value)
    || value < 0
    || (!allowZero && value === 0)
  ) {
    throw new Error(invalidResponseMessage);
  }
  return value;
}

function parseSummary(value: unknown): ErrorDiagnosticsSummary | null {
  if (value === null) return null;
  const record = requireRecord(value);
  return {
    lastHour: requireCount(record, 'lastHour'),
    last24Hours: requireCount(record, 'last24Hours'),
    last7Days: requireCount(record, 'last7Days'),
    frontendLast24Hours: requireCount(record, 'frontendLast24Hours'),
    backendLast24Hours: requireCount(record, 'backendLast24Hours'),
  };
}

function parseTelemetryHealth(value: unknown): ErrorDiagnosticsTelemetryHealth | null {
  if (value === null) return null;
  const record = requireRecord(value);
  return {
    frontendLastSeenUtc: nullableTimestamp(record, 'frontendLastSeenUtc'),
    backendLastSeenUtc: nullableTimestamp(record, 'backendLastSeenUtc'),
  };
}

function parseItem(value: unknown): ErrorDiagnosticsItem {
  const record = requireRecord(value);
  const source = requireString(record, 'source');
  const severity = requireString(record, 'severity');
  const timestampUtc = requireTimestamp(record, 'timestampUtc');
  const firstSeenUtc = requireTimestamp(record, 'firstSeenUtc');
  const lastSeenUtc = requireTimestamp(record, 'lastSeenUtc');

  if (source !== 'frontend' && source !== 'backend') throw new Error(invalidResponseMessage);
  if (severity !== 'error' && severity !== 'critical') throw new Error(invalidResponseMessage);
  if (Date.parse(firstSeenUtc) > Date.parse(lastSeenUtc) || timestampUtc !== lastSeenUtc) {
    throw new Error(invalidResponseMessage);
  }

  return {
    timestampUtc,
    firstSeenUtc,
    lastSeenUtc,
    source,
    severity,
    errorType: requireString(record, 'errorType'),
    fingerprint: requireString(record, 'fingerprint'),
    message: requireString(record, 'message'),
    route: nullableString(record, 'route'),
    operation: nullableString(record, 'operation'),
    release: nullableString(record, 'release'),
    correlationId: nullableString(record, 'correlationId'),
    traceId: nullableString(record, 'traceId'),
    affectedReleaseCount: requireCount(record, 'affectedReleaseCount'),
    affectedRouteCount: requireCount(record, 'affectedRouteCount'),
    affectedOperationCount: requireCount(record, 'affectedOperationCount'),
    occurrences: requireCount(record, 'occurrences', false),
  };
}

export function parseErrorDiagnosticsDashboard(value: unknown): ErrorDiagnosticsDashboard {
  const record = requireRecord(value);
  const itemsValue = record.items;
  if (!Array.isArray(itemsValue)) throw new Error(invalidResponseMessage);

  const dashboard: ErrorDiagnosticsDashboard = {
    isAvailable: requireBoolean(record, 'isAvailable'),
    isComplete: requireBoolean(record, 'isComplete'),
    isStale: requireBoolean(record, 'isStale'),
    availabilityReason: nullableString(record, 'availabilityReason'),
    generatedAtUtc: requireTimestamp(record, 'generatedAtUtc'),
    dataRetrievedAtUtc: nullableTimestamp(record, 'dataRetrievedAtUtc'),
    summaryAvailable: requireBoolean(record, 'summaryAvailable'),
    itemsAvailable: requireBoolean(record, 'itemsAvailable'),
    telemetryHealthAvailable: requireBoolean(record, 'telemetryHealthAvailable'),
    hasPartialAzureResults: requireBoolean(record, 'hasPartialAzureResults'),
    isTruncated: requireBoolean(record, 'isTruncated'),
    summary: parseSummary(record.summary),
    telemetryHealth: parseTelemetryHealth(record.telemetryHealth),
    items: itemsValue.map(parseItem),
  };

  if (dashboard.summaryAvailable !== (dashboard.summary !== null)) {
    throw new Error(invalidResponseMessage);
  }
  if (dashboard.telemetryHealthAvailable !== (dashboard.telemetryHealth !== null)) {
    throw new Error(invalidResponseMessage);
  }
  if (!dashboard.itemsAvailable && dashboard.items.length > 0) {
    throw new Error(invalidResponseMessage);
  }
  if (
    !dashboard.isAvailable
    && (dashboard.summaryAvailable
      || dashboard.itemsAvailable
      || dashboard.telemetryHealthAvailable)
  ) {
    throw new Error(invalidResponseMessage);
  }
  if (
    dashboard.isComplete
    && (!dashboard.summaryAvailable
      || !dashboard.itemsAvailable
      || !dashboard.telemetryHealthAvailable
      || dashboard.isStale
      || dashboard.hasPartialAzureResults)
  ) {
    throw new Error(invalidResponseMessage);
  }

  return dashboard;
}

export async function getErrorDiagnostics(
  range: ErrorDiagnosticsRange,
  source: ErrorDiagnosticsSource,
): Promise<ErrorDiagnosticsDashboard> {
  const response = await apiClient.get('/api/admin/diagnostics/errors', {
    params: { range, source, limit: 50 },
    skipGlobalErrorToast: true,
  });
  return parseErrorDiagnosticsDashboard(response);
}
