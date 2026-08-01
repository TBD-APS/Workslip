from pathlib import Path

job_list_path = Path("src/FE/src/features/jobs/routes/JobList.tsx")
job_list = job_list_path.read_text(encoding="utf-8-sig")
old = '{job.status === JobStatus.Approved && !job.isSeen && <span className="approved-dot" />}'
new = '{job.status === JobStatus.Approved && <span className="approved-dot" />}'
count = job_list.count(old)
if count != 2:
    raise SystemExit(f"Expected two approved-dot conditions, found {count}")
job_list_path.write_text(job_list.replace(old, new), encoding="utf-8")

Path("src/FE/src/features/jobs/routes/JobList.completed-dot.test.tsx").write_text(
    """import { cleanup, render } from '@testing-library/react';
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
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    reportDate: '2026-08-01',
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
""",
    encoding="utf-8",
)

Path(".github/scripts/wor-246-status-dot-update.py").unlink(missing_ok=True)
Path(".github/workflows/wor-246-status-dot-update.yml").unlink(missing_ok=True)
