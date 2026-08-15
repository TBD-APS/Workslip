import { fireEvent, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useGetApiJobsIdHistory } from '../../../api/generated/jobs/jobs';
import { JobHistoryDrawer } from './JobHistoryDrawer';

vi.mock('../../../api/generated/jobs/jobs', () => ({
  useGetApiJobsIdHistory: vi.fn(),
}));

vi.mock('../../../components/common/Drawer', () => ({
  Drawer: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

describe('JobHistoryDrawer', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-08-15T18:00:00.000Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('shows domain history as an actor-first activity row and expands field changes accessibly', () => {
    const refetch = vi.fn();
    vi.mocked(useGetApiJobsIdHistory).mockReturnValue({
      data: [{
        id: 'history-1',
        actorId: 'user-1',
        actorName: 'Rasmus Petersen',
        eventType: 'Modified',
        summary: 'Status blev ændret.',
        changes: [{
          propertyName: 'Status',
          displayName: 'Status',
          before: 'Aktiv',
          after: 'Godkendt',
        }],
        createdAt: '2026-08-15T17:00:00.000Z',
      }],
      isLoading: false,
      refetch,
    } as unknown as ReturnType<typeof useGetApiJobsIdHistory>);

    render(<JobHistoryDrawer jobId="job-1" isOpen onClose={vi.fn()} />);

    expect(screen.getByText('I dag')).toBeInTheDocument();
    expect(screen.getByText('Rasmus Petersen')).toBeInTheDocument();
    expect(screen.getByText('Status blev ændret.')).toBeInTheDocument();

    const trigger = screen.getByRole('button', {
      name: 'Rasmus Petersen: Ændret. 1 feltændring. Vis detaljer',
    });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(trigger);

    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Før')).toBeInTheDocument();
    expect(screen.getByText('Aktiv')).toBeInTheDocument();
    expect(screen.getByText('Efter')).toBeInTheDocument();
    expect(screen.getByText('Godkendt')).toBeInTheDocument();
  });
});
