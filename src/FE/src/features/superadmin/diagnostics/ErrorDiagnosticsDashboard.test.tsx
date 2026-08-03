import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ErrorCard } from './ErrorDiagnosticsDashboard';
import type { ErrorDiagnosticsItem } from './types';

const groupedError: ErrorDiagnosticsItem = {
  timestampUtc: '2026-08-03T14:45:00.000Z',
  firstSeenUtc: '2026-08-01T09:10:00.000Z',
  lastSeenUtc: '2026-08-03T14:45:00.000Z',
  source: 'frontend',
  severity: 'error',
  errorType: 'TypeError [da1fc4b7] at wa',
  fingerprint: 'afd20854d22a',
  message: 'TypeError [da1fc4b7]',
  route: '/superadmin',
  operation: '/app',
  release: 'release-3',
  correlationId: null,
  traceId: '697a3e37e3344cefbb182df1972dcc2e',
  affectedReleaseCount: 3,
  affectedRouteCount: 2,
  affectedOperationCount: 2,
  occurrences: 7,
};

describe('ErrorCard', () => {
  it('shows the grouped time span, occurrence count and affected context counts', () => {
    render(<ErrorCard item={groupedError} />);

    expect(screen.getByRole('heading', { name: groupedError.errorType })).toBeInTheDocument();
    expect(screen.getByLabelText('7 forekomster')).toHaveTextContent('×7');
    expect(screen.getByText('/superadmin')).toBeInTheDocument();
    expect(screen.getByText('release-3')).toBeInTheDocument();

    const metadata = screen.getByText('Først set').closest('dl');
    expect(metadata).not.toBeNull();
    const metadataList = within(metadata!);
    expect(metadataList.getByText('Berørte releases').parentElement).toHaveTextContent('3');
    expect(metadataList.getByText('Berørte routes').parentElement).toHaveTextContent('2');
    expect(metadataList.getByText('Berørte operationer').parentElement).toHaveTextContent('2');
    const timestamps = Array.from(metadata!.querySelectorAll('time'));
    expect(timestamps.some((time) => time.getAttribute('datetime') === groupedError.firstSeenUtc)).toBe(true);
    expect(timestamps.some((time) => time.getAttribute('datetime') === groupedError.lastSeenUtc)).toBe(true);
  });
});
