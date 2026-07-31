import { cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { JobListItemViewModel } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models/jobStatus';
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
    createdAt: '2026-07-31T00:00:00Z',
    updatedAt: '2026-07-31T00:00:00Z',
    reportDate: '2026-07-31',
    jobType: 'KLS',
    destinationAddress: 'Testvej 2',
    destinationZipCode: '8000',
    destinationCity: 'Aarhus C',
    taskDescription: 'Testopgave',
    isSeen: false,
    isNewRejection: false,
    rejectionNote: null,
    ...overrides,
  };
}

afterEach(() => {
  cleanup();
});

describe('JobCard completed indicator', () => {
  it('shows the completed dot to a regular user until the approved job is seen', () => {
    const onOpen = vi.fn();
    const { container, rerender } = render(
      <JobCard job={createJob()} isAdmin={false} onOpen={onOpen} />,
    );

    expect(container.querySelector('.approved-dot')).toBeInTheDocument();

    rerender(
      <JobCard job={createJob({ isSeen: true })} isAdmin={false} onOpen={onOpen} />,
    );

    expect(container.querySelector('.approved-dot')).not.toBeInTheDocument();
  });

  it('does not use the completed dot for a non-approved unread job', () => {
    const { container } = render(
      <JobCard
        job={createJob({ status: JobStatus.InReview, isSeen: false })}
        isAdmin={false}
        onOpen={vi.fn()}
      />,
    );

    expect(container.querySelector('.approved-dot')).not.toBeInTheDocument();
  });
});
