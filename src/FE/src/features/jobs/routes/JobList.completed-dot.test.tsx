import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JobStatus, type JobListItemViewModel } from '../../../api/generated/models';
import { JobCard } from './JobList';

function createJob(overrides: Partial<JobListItemViewModel> = {}): JobListItemViewModel {
  return {
    id: 'job-1',
    organizationId: 'organization-1',
    customer: {
      customerId: 'customer-1',
      name: 'Kunde A/S',
      address: 'Testvej 1',
      email: 'kunde@example.com',
      contactPerson: 'Test Person',
      phone: '12345678',
    },
    reportNumber: '0001',
    status: JobStatus.Approved,
    installationTypes: [],
    assignedUsers: [{ id: 'user-1', displayName: 'Testbruger' }],
    softDeleted: false,
    totalHours: 1,
    updatedAt: '2026-08-01T00:00:00Z',
    reportDate: '2026-08-01',
    jobType: 'KLS',
    destinationAddress: 'Testvej 2',
    taskDescription: 'Testopgave',
    isSeen: false,
    isNewRejection: false,
    ...overrides,
  };
}

afterEach(() => {
  cleanup();
});

describe('JobCard', () => {
  it('shows the last updated date and time', () => {
    render(
      <JobCard
        job={createJob({ updatedAt: '2026-08-01T12:34:00' })}
        isAdmin={false}
        onOpen={vi.fn()}
      />,
    );

    expect(screen.getByText('Opdateret 01.08.2026, 12.34')).toBeInTheDocument();
  });

  it('keeps the completed dot after the approved job is marked as seen', () => {
    const onOpen = vi.fn();
    const { container, rerender } = render(
      <JobCard job={createJob()} isAdmin={false} onOpen={onOpen} />,
    );

    expect(container.querySelector('.approved-dot')).toBeInTheDocument();

    rerender(
      <JobCard job={createJob({ isSeen: true })} isAdmin={false} onOpen={onOpen} />,
    );

    expect(container.querySelector('.approved-dot')).toBeInTheDocument();
  });

  it('removes the completed dot when the job status changes', () => {
    const { container, rerender } = render(
      <JobCard job={createJob({ isSeen: true })} isAdmin={false} onOpen={vi.fn()} />,
    );

    expect(container.querySelector('.approved-dot')).toBeInTheDocument();

    rerender(
      <JobCard
        job={createJob({ status: JobStatus.InReview, isSeen: true })}
        isAdmin={false}
        onOpen={vi.fn()}
      />,
    );

    expect(container.querySelector('.approved-dot')).not.toBeInTheDocument();
  });
});
