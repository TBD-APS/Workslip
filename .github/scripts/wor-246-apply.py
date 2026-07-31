from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    content = file_path.read_text(encoding="utf-8-sig")
    count = content.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match in {path}, found {count}")
    file_path.write_text(content.replace(old, new, 1), encoding="utf-8")


Path("src/BE/WorkslipApi/Workslip.Application/Jobs/JobViewTypes.cs").write_text(
    """using Workslip.Domain;

namespace Workslip.Application.Jobs;

public static class JobViewTypes
{
    public const string New = \"New\";
    public const string Completed = \"Completed\";

    public static bool IsSeen(JobStatus status, bool hasNewView, bool hasCompletedView) =>
        status == JobStatus.Approved ? hasCompletedView : hasNewView;
}
""",
    encoding="utf-8",
)

replace_once(
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs",
    '            await _jobViewRepository.MarkAsViewedAsync(id, currentUser.UserId!.Value, "New", cancellationToken);',
    '            await _jobViewRepository.MarkAsViewedAsync(id, actorId.Value, JobViewTypes.Completed, cancellationToken);',
)
replace_once(
    "src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs",
    '        await _jobViewRepository.MarkAsViewedAsync(id, userId.Value, viewType ?? "New", cancellationToken);',
    '        await _jobViewRepository.MarkAsViewedAsync(id, userId.Value, viewType ?? JobViewTypes.New, cancellationToken);',
)

replace_once(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    '''        HashSet<Guid> seenJobIds = [];
        if (query.CurrentUserId.HasValue && reportIds.Length > 0)
        {
            var viewed = await _jobViewRepo.GetViewedJobIdsAsync(
                query.CurrentUserId.Value, reportIds, ["New"], cancellationToken);
            seenJobIds = new HashSet<Guid>(viewed);
        }
''',
    '''        HashSet<Guid> seenNewJobIds = [];
        HashSet<Guid> seenCompletedJobIds = [];
        if (query.CurrentUserId.HasValue && reportIds.Length > 0)
        {
            var viewedNewJobs = await _jobViewRepo.GetViewedJobIdsAsync(
                query.CurrentUserId.Value, reportIds, [JobViewTypes.New], cancellationToken);
            seenNewJobIds = new HashSet<Guid>(viewedNewJobs);

            var viewedCompletedJobs = await _jobViewRepo.GetViewedJobIdsAsync(
                query.CurrentUserId.Value, reportIds, [JobViewTypes.Completed], cancellationToken);
            seenCompletedJobIds = new HashSet<Guid>(viewedCompletedJobs);
        }
''',
)
replace_once(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    '''            var isNewRejection = status == JobStatus.Rejected
                && isAssignedToCurrentUser;

            return new JobListItemResponse(
''',
    '''            var isNewRejection = status == JobStatus.Rejected
                && isAssignedToCurrentUser;
            var isSeenByCurrentUser = JobViewTypes.IsSeen(
                status,
                seenNewJobIds.Contains(x.Id),
                seenCompletedJobIds.Contains(x.Id));

            return new JobListItemResponse(
''',
)
replace_once(
    "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs",
    '''                totalHoursByJob.GetValueOrDefault(x.Id),
                seenJobIds.Contains(x.Id),
                isNewRejection,
''',
    '''                totalHoursByJob.GetValueOrDefault(x.Id),
                isSeenByCurrentUser,
                isNewRejection,
''',
)

replace_once(
    "src/FE/src/features/jobs/utils/markJobSeen.ts",
    "import { getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';\n",
    "import { getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';\n\nexport const COMPLETED_JOB_VIEW_TYPE = 'Completed';\n",
)
replace_once(
    "src/FE/src/features/jobs/routes/CompletedJobReport.tsx",
    "import { markJobAsSeen } from '../utils/markJobSeen';",
    "import { COMPLETED_JOB_VIEW_TYPE, markJobAsSeen } from '../utils/markJobSeen';",
)
replace_once(
    "src/FE/src/features/jobs/routes/CompletedJobReport.tsx",
    '''  useEffect(() => {
    if (!id) return;
    markJobAsSeen(id, queryClient);
  }, [id, queryClient]);
''',
    '''  useEffect(() => {
    if (!id || !job) return;
    const viewType = job.status === JobStatus.Approved ? COMPLETED_JOB_VIEW_TYPE : undefined;
    markJobAsSeen(id, queryClient, viewType);
  }, [id, job?.status, queryClient]);
''',
)

frontend_test = Path("src/FE/src/features/jobs/routes/CompletedJobReport.seen-state.test.tsx")
frontend_content = frontend_test.read_text(encoding="utf-8")
closing = "\n});\n"
if not frontend_content.endswith(closing):
    raise SystemExit("Unexpected CompletedJobReport seen-state test ending")
frontend_test.write_text(
    frontend_content[:-len(closing)]
    + '''

  it('marks an approved report with the completed view type for an ordinary user', async () => {
    mocks.isAdmin = false;
    mocks.job = createJob(JobStatus.Approved);
    renderReport();

    await waitFor(() => {
      expect(mocks.markJobAsSeen).toHaveBeenCalledOnce();
    });
    expect(mocks.markJobAsSeen).toHaveBeenCalledWith(
      'job-1',
      expect.any(QueryClient),
      'Completed',
    );
  });
'''
    + closing,
    encoding="utf-8",
)

Path("src/BE/WorkslipApi/Workslip.Tests/Jobs/JobViewTypesTests.cs").write_text(
    """using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class JobViewTypesTests
{
    [Fact]
    public void Approved_job_requires_completed_view_even_when_job_was_seen_before_approval()
    {
        var isSeen = JobViewTypes.IsSeen(
            JobStatus.Approved,
            hasNewView: true,
            hasCompletedView: false);

        Assert.False(isSeen);
    }

    [Fact]
    public void Approved_job_is_seen_after_completed_view()
    {
        var isSeen = JobViewTypes.IsSeen(
            JobStatus.Approved,
            hasNewView: true,
            hasCompletedView: true);

        Assert.True(isSeen);
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.InReview)]
    [InlineData(JobStatus.Rejected)]
    public void Non_approved_job_uses_normal_view(JobStatus status)
    {
        var isSeen = JobViewTypes.IsSeen(
            status,
            hasNewView: true,
            hasCompletedView: false);

        Assert.True(isSeen);
    }
}
""",
    encoding="utf-8",
)

replace_once(
    "Docs/architecture/domain-and-dataflows.md",
    "| Push subscriptions, notification queue and job views | `UserId` must resolve to an existing user. These tables do not currently duplicate `OrganizationId`; tenant authorization is enforced before their rows are written or queried. |\n",
    "| Push subscriptions, notification queue and job views | `UserId` must resolve to an existing user. These tables do not currently duplicate `OrganizationId`; tenant authorization is enforced before their rows are written or queried. |\n\nJob views use separate `ViewType` acknowledgements for the ordinary job view (`New`) and the post-approval view (`Completed`). An approved job is therefore unread for an assigned user until that user opens the approved report, even when the same user viewed the job while completing it.\n",
)
