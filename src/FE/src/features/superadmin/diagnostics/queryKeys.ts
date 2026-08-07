import type { ErrorDiagnosticsRange, ErrorDiagnosticsSource } from './types';

export const errorDiagnosticsQueryPrefix = ['superadmin', 'diagnostics', 'errors'] as const;

export const errorDiagnosticsQueryKey = (
  range: ErrorDiagnosticsRange,
  source: ErrorDiagnosticsSource,
) => [...errorDiagnosticsQueryPrefix, range, source] as const;
