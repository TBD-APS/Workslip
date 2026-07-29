using Microsoft.AspNetCore.Http;
using Workslip.Api.Endpoints;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;
using JobListItemViewModel = Workslip.Api.ViewModels.JobListItemViewModel;
using JobListViewModel = Workslip.Api.ViewModels.JobListViewModel;

namespace Workslip.Tests.Configuration;

public sealed class HttpCacheHeadersTests
{
    [Fact]
    public void SetNoStore_sets_all_mutation_cache_headers()
    {
        var context = new DefaultHttpContext();

        HttpCacheHeaders.SetNoStore(context);

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Equal("0", context.Response.Headers.Expires.ToString());
    }

    [Fact]
    public void JobReportEtag_changes_when_links_change()
    {
        var report = CreateReport(links: []);
        var changedReport = report with
        {
            Links =
            [
                new JobLinkInfoResponse(Guid.NewGuid(), Guid.NewGuid(), "2026-001", "ACME", "", JobStatus.Draft.ToString())
            ]
        };

        Assert.NotEqual(HttpCacheHeaders.JobReportEtag(report), HttpCacheHeaders.JobReportEtag(changedReport));
    }

    [Fact]
    public void JobReportEtag_changes_when_assigned_users_change()
    {
        var report = CreateReport(assignedUsers: []);
        var changedReport = report with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Ada")]
        };

        Assert.NotEqual(HttpCacheHeaders.JobReportEtag(report), HttpCacheHeaders.JobReportEtag(changedReport));
    }

    [Fact]
    public void JobListEtag_changes_when_assignments_change()
    {
        var response = CreateJobList();
        var item = response.Items.Single();
        var changed = response with
        {
            Items = [item with { AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Ada")] }]
        };

        Assert.NotEqual(CreateJobListEtag(response), CreateJobListEtag(changed));
    }

    [Fact]
    public void JobListEtag_changes_when_total_hours_change()
    {
        var response = CreateJobList();
        var item = response.Items.Single();
        var changed = response with
        {
            Items = [item with { TotalHours = 7.5m }]
        };

        Assert.NotEqual(CreateJobListEtag(response), CreateJobListEtag(changed));
    }

    [Fact]
    public void JobListEtag_changes_when_installations_change()
    {
        var response = CreateJobList();
        var item = response.Items.Single();
        var changed = response with
        {
            Items = [item with { InstallationTypes = ["Varmepumpe"] }]
        };

        Assert.NotEqual(CreateJobListEtag(response), CreateJobListEtag(changed));
    }

    [Fact]
    public void JobListEtag_changes_when_seen_state_changes()
    {
        var response = CreateJobList();
        var item = response.Items.Single();
        var changed = response with
        {
            Items = [item with { IsSeen = true }]
        };

        Assert.NotEqual(CreateJobListEtag(response), CreateJobListEtag(changed));
    }

    [Fact]
    public void JobAssignedEtag_changes_when_related_list_data_changes()
    {
        var response = CreateJobList();
        var item = response.Items.Single();
        var changed = response with
        {
            Items = [item with { TotalHours = 9m, InstallationTypes = ["Kedel"] }]
        };

        Assert.NotEqual(CreateAssignedJobsEtag(response), CreateAssignedJobsEtag(changed));
    }

    private static string CreateJobListEtag(JobListViewModel response) =>
        HttpCacheHeaders.JobListEtag(response, response.Items.Single().OrganizationId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static string CreateAssignedJobsEtag(JobListViewModel response) =>
        HttpCacheHeaders.JobAssignedEtag(response.Items, response.Items.Single().OrganizationId, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static JobListViewModel CreateJobList()
    {
        var organizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var item = new JobListItemViewModel(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            organizationId,
            null,
            "2026-0001",
            JobStatus.Draft,
            [],
            [],
            false,
            1.5m,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "Diverse",
            null,
            null,
            null,
            null,
            false,
            false,
            null);

        return new JobListViewModel([item], 1);
    }

    private static JobReportSummaryResponse CreateReport(
        IReadOnlyList<JobLinkInfoResponse>? links = null,
        IReadOnlyList<AssignedUserResponse>? assignedUsers = null)
    {
        return new JobReportSummaryResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RBJ Teknisk",
            "12345678",
            "2026-000",
            JobStatus.Draft,
            null,
            new CustomerSnapshotResponse(null, null, null, null, null),
            null,
            null,
            null,
            "Diverse",
            new JobReportSummaryWorkResponse(null, [], [], null),
            new JobReportSummaryObservationResponse(null, null, null),
            [],
            links ?? [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            assignedUsers ?? [],
            [],
            null,
            null,
            false,
            null);
    }
}
