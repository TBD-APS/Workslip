import { apiClient } from '../../../lib/axios';
import type {
  ErrorDiagnosticsDashboard,
  ErrorDiagnosticsRange,
  ErrorDiagnosticsSource,
} from './types';

export const errorDiagnosticsQueryKey = (
  range: ErrorDiagnosticsRange,
  source: ErrorDiagnosticsSource,
) => ['superadmin', 'diagnostics', 'errors', range, source] as const;

export async function getErrorDiagnostics(
  range: ErrorDiagnosticsRange,
  source: ErrorDiagnosticsSource,
): Promise<ErrorDiagnosticsDashboard> {
  return await apiClient.get('/api/admin/diagnostics/errors', {
    params: { range, source, limit: 50 },
    skipGlobalErrorToast: true,
  }) as unknown as ErrorDiagnosticsDashboard;
}
