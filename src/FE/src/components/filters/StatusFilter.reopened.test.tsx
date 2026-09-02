import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getSavedStatusFilter, StatusFilter } from './StatusFilter';
import { JobStatus } from '../../api/generated/models/jobStatus';

afterEach(cleanup);

class ResizeObserverMock {
  observe() {}
  unobserve() {}
  disconnect() {}
}

beforeEach(() => {
  sessionStorage.clear();
  window.history.replaceState({}, '', '/');
  vi.stubGlobal('ResizeObserver', ResizeObserverMock);
});

describe('StatusFilter Reopened', () => {
  it('renders Genåbnet option when Reopened is in options', () => {
    const options = [
      { value: JobStatus.Draft, label: 'Aktiv' },
      { value: JobStatus.InReview, label: 'Til gennemsyn' },
      { value: JobStatus.Approved, label: 'Godkendt' },
      { value: JobStatus.Rejected, label: 'Afvist' },
      { value: JobStatus.Reopened, label: 'Genåbnet' },
    ];
    render(<StatusFilter options={options} selected={[JobStatus.Reopened]} onChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Genåbnet' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Aktiv' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('persists Reopened via getSavedStatusFilter from URL', () => {
    sessionStorage.setItem('statusFilter:lastActive', 'mine-jobs');
    window.history.replaceState({}, '', '/app?status=Reopened');

    // JOB_STATUS_QUERY_VALUES now includes Reopened – URL param should be honoured
    expect(getSavedStatusFilter('mine-jobs', [JobStatus.Draft])).toEqual([JobStatus.Reopened]);
    expect(sessionStorage.getItem('statusFilter:mine-jobs')).toBe(JSON.stringify([JobStatus.Reopened]));
  });

  it('keeps Reopened when stored in sessionStorage', () => {
    sessionStorage.setItem('statusFilter:lastActive', 'mine-jobs');
    sessionStorage.setItem('statusFilter:mine-jobs', JSON.stringify([JobStatus.Reopened]));

    expect(getSavedStatusFilter('mine-jobs', [JobStatus.Draft])).toEqual([JobStatus.Reopened]);
  });
});
