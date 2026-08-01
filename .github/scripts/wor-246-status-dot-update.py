from pathlib import Path
import subprocess


def run(*args: str) -> None:
    subprocess.run(args, check=True)


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    content = file_path.read_text(encoding="utf-8-sig")
    count = content.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match in {path}, found {count}")
    file_path.write_text(content.replace(old, new, 1), encoding="utf-8")


run("git", "fetch", "origin", "main", "--depth=1")
run(
    "git",
    "checkout",
    "origin/main",
    "--",
    "Docs/architecture/domain-and-dataflows.md",
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs",
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    "src/FE/src/features/jobs/routes/CompletedJobReport.seen-state.test.tsx",
    "src/FE/src/features/jobs/routes/CompletedJobReport.tsx",
    "src/FE/src/features/jobs/utils/markJobSeen.ts",
)

for path in (
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobViewTypes.cs",
    "src/BE/WorkslipApi/Workslip.Tests/Jobs/JobViewTypesTests.cs",
):
    Path(path).unlink(missing_ok=True)

replace_once(
    "src/FE/src/features/jobs/routes/JobList.tsx",
    '{job.status === JobStatus.Approved && !job.isSeen && <span className="approved-dot" />}',
    '{job.status === JobStatus.Approved && <span className="approved-dot" />}',
)

Path("src/FE/src/features/jobs/routes/JobList.completed-dot.test.tsx").write_text(
    """import { cleanup, render } from '@testing-library/react';
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
