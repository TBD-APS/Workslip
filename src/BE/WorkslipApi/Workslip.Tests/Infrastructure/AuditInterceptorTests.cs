using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Mappers;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class AuditInterceptorTests
{
    [Fact]
    public async Task Job_assignment_audit_uses_current_user_as_actor_and_assigned_user_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = assignedUserId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.JobAssignments.Add(new JobAssignmentRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ReportId = jobId,
            UserId = assignedUserId,
            AssignedByUserId = actorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal("Tech Tim assigned", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        Assert.Equal("Planner Pia", response.ActorName);
        var change = Assert.Single(response.Changes);
        Assert.Equal("AssignedUser", change.PropertyName);
        Assert.Equal("Tildelt bruger", change.DisplayName);
        Assert.Null(change.Before);
        Assert.Equal("Tech Tim", change.After);
    }

    [Fact]
    public async Task Job_assignment_delete_audit_uses_removed_user_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = assignedUserId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobAssignments.Add(new JobAssignmentRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ReportId = jobId,
            UserId = assignedUserId,
            AssignedByUserId = actorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.JobAssignments.Remove(await context.JobAssignments.SingleAsync());
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal("Tech Tim unassigned", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("AssignedUser", change.PropertyName);
        Assert.Equal("Tech Tim", change.Before);
        Assert.Null(change.After);
    }

    [Fact]
    public async Task Assignment_repository_removal_creates_unassigned_audit_event()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var removedUserId = Guid.NewGuid();
        var keptUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = removedUserId, OrganizationId = orgId, DisplayName = "Removed Ron", Email = "ron@example.test", Role = "User" },
            new UserDataRow { Id = keptUserId, OrganizationId = orgId, DisplayName = "Kept Kim", Email = "kim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobAssignments.AddRange(
            new JobAssignmentRow { Id = Guid.NewGuid(), OrganizationId = orgId, ReportId = jobId, UserId = removedUserId, AssignedByUserId = actorId, AssignedAt = DateTimeOffset.UtcNow },
            new JobAssignmentRow { Id = Guid.NewGuid(), OrganizationId = orgId, ReportId = jobId, UserId = keptUserId, AssignedByUserId = actorId, AssignedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var repository = new EfAssignmentRepository(
            context,
            new NoRetryPolicy(),
            new TestCurrentUserContext(actorId, orgId),
            worksheetRepo: null!);

        await repository.AssignAsync(jobId, orgId, [keptUserId], actorId, CancellationToken.None);

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Removed Ron unassigned", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("AssignedUser", change.PropertyName);
        Assert.Equal("Removed Ron", change.Before);
        Assert.Null(change.After);
    }

    [Fact]
    public async Task Job_link_delete_audit_shows_removed_link_on_both_reports()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var sourceReportId = Guid.NewGuid();
        var targetReportId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedReportsWithLinks(context, orgId,
            [(sourceReportId, "JOB-1"), (targetReportId, "JOB-2")],
            [new JobReportLinkRow
            {
                Id = linkId,
                OrganizationId = orgId,
                SourceReportId = sourceReportId,
                TargetReportId = targetReportId,
                CreatedAt = DateTimeOffset.UtcNow
            }]);

        context.JobReportLinks.Remove(await context.JobReportLinks.SingleAsync());
        await context.SaveChangesAsync();

        var events = await context.JobEvents.AsNoTracking().OrderBy(e => e.ReportId).ToListAsync();
        Assert.Equal(2, events.Count);

        var sourceEvent = Assert.Single(events, e => e.ReportId == sourceReportId);
        Assert.Equal("Link to JOB-2 removed", sourceEvent.Summary);
        Assert.Equal("JOB-2", Assert.Single(JobReportMapper.ToHistoryResponse(sourceEvent, "Planner Pia").Changes).Before);

        var targetEvent = Assert.Single(events, e => e.ReportId == targetReportId);
        Assert.Equal("Link to JOB-1 removed", targetEvent.Summary);
        Assert.Equal("JOB-1", Assert.Single(JobReportMapper.ToHistoryResponse(targetEvent, "Planner Pia").Changes).Before);
    }

    [Fact]
    public async Task Job_link_repository_can_audit_multiple_removed_links_in_one_call()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var sourceReportId = Guid.NewGuid();
        var firstTargetReportId = Guid.NewGuid();
        var secondTargetReportId = Guid.NewGuid();
        var firstLinkId = Guid.NewGuid();
        var secondLinkId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        SeedReportsWithLinks(context, orgId,
            [(sourceReportId, "JOB-1"), (firstTargetReportId, "JOB-2"), (secondTargetReportId, "JOB-3")],
            [
                new JobReportLinkRow { Id = firstLinkId, OrganizationId = orgId, SourceReportId = sourceReportId, TargetReportId = firstTargetReportId, CreatedAt = DateTimeOffset.UtcNow },
                new JobReportLinkRow { Id = secondLinkId, OrganizationId = orgId, SourceReportId = sourceReportId, TargetReportId = secondTargetReportId, CreatedAt = DateTimeOffset.UtcNow }
            ]);

        var repository = new EfJobLinkRepository(context, new NoRetryPolicy());
        await repository.DeleteLinksAsync(orgId, [firstLinkId, secondLinkId], CancellationToken.None);

        var events = await context.JobEvents.AsNoTracking().ToListAsync();
        Assert.Equal(4, events.Count);
        Assert.Contains(events, e => e.ReportId == sourceReportId && e.Summary == "Link to JOB-2 removed");
        Assert.Contains(events, e => e.ReportId == sourceReportId && e.Summary == "Link to JOB-3 removed");
        Assert.Contains(events, e => e.ReportId == firstTargetReportId && e.Summary == "Link to JOB-1 removed");
        Assert.Contains(events, e => e.ReportId == secondTargetReportId && e.Summary == "Link to JOB-1 removed");
    }


    [Fact]
    public async Task Job_report_work_kind_audit_uses_work_kind_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var firstWorkKindId = Guid.NewGuid();
        var secondWorkKindId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobWorkKinds.AddRange(
            new JobWorkKindRow { Id = firstWorkKindId, NormalizedLabel = "service", Label = "Service", IsActive = true, SortOrder = 1 },
            new JobWorkKindRow { Id = secondWorkKindId, NormalizedLabel = "repair", Label = "Reparation", IsActive = true, SortOrder = 2 });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            WorkKindId = firstWorkKindId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.WorkKindId).CurrentValue = secondWorkKindId;
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("WorkKind changed: 'Service' → 'Reparation'", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("WorkKind", change.PropertyName);
        Assert.Equal("Opgavetype", change.DisplayName);
        Assert.Equal("Service", change.Before);
        Assert.Equal("Reparation", change.After);
    }

    [Fact]
    public async Task Job_report_added_audit_uses_work_kind_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var workKindId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobWorkKinds.Add(new JobWorkKindRow { Id = workKindId, NormalizedLabel = "service", Label = "Service", IsActive = true, SortOrder = 1 });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            WorkKindId = workKindId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes, c => c.PropertyName == "WorkKind");
        Assert.Equal("Opgavetype", change.DisplayName);
        Assert.Null(change.Before);
        Assert.Equal("Service", change.After);
        Assert.DoesNotContain(response.Changes, c => c.PropertyName == "WorkKindId");
    }



    [Fact]
    public async Task Job_report_customer_audit_uses_customer_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var firstCustomerId = Guid.NewGuid();
        var secondCustomerId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Customers.AddRange(
            new CustomerRow { Id = firstCustomerId, OrganizationId = orgId, Name = "Kunde A", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
            new CustomerRow { Id = secondCustomerId, OrganizationId = orgId, Name = "Kunde B", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            CustomerId = firstCustomerId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.CustomerId).CurrentValue = secondCustomerId;
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("Customer", change.PropertyName);
        Assert.Equal("Kunde", change.DisplayName);
        Assert.Equal("Kunde A", change.Before);
        Assert.Equal("Kunde B", change.After);
        Assert.DoesNotContain(firstCustomerId.ToString(), response.Summary);
        Assert.DoesNotContain(secondCustomerId.ToString(), response.Summary);
    }

    [Fact]
    public async Task Job_report_installation_type_add_audit_uses_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var installationTypeId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.InstallationTypeDefinitions.Add(new InstallationTypeDefinitionRow
        {
            Id = installationTypeId,
            OrganizationId = orgId,
            Name = "Gasinstallation",
            SortOrder = 1
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.JobReportInstallations.Add(new JobReportInstallationRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            JobReportId = jobId,
            InstallationTypeDefinitionId = installationTypeId,
            SortOrder = 1
        });
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Installation type Gasinstallation added", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("InstallationType", change.PropertyName);
        Assert.Equal("Anlægstype", change.DisplayName);
        Assert.Null(change.Before);
        Assert.Equal("Gasinstallation", change.After);
    }

    [Fact]
    public async Task Job_report_control_point_audit_uses_control_point_display_name_when_checked_changes()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var installationTypeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var controlPointId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var installationCategoryId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.InstallationTypeDefinitions.Add(new InstallationTypeDefinitionRow { Id = installationTypeId, OrganizationId = orgId, Name = "Gasinstallation", SortOrder = 1 });
        context.ControlCategoryRow.Add(new ControlCategoryRow { Id = categoryId, Name = "Modtagekontrol", SortOrder = 1 });
        context.ControlPointRow.Add(new ControlPointRow { Id = controlPointId, Name = "Trykprøvning", SortOrder = 1 });
        context.JobReportInstallations.Add(new JobReportInstallationRow
        {
            Id = installationId,
            OrganizationId = orgId,
            JobReportId = jobId,
            InstallationTypeDefinitionId = installationTypeId,
            SortOrder = 1
        });
        context.JobReportInstallationCategories.Add(new JobReportInstallationCategoryRow
        {
            Id = installationCategoryId,
            JobReportInstallationId = installationId,
            ControlCategoryId = categoryId,
            SortOrder = 1
        });
        context.JobReportInstallationControlPoints.Add(new JobReportInstallationControlPointRow
        {
            JobReportInstallationCategoryId = installationCategoryId,
            ControlPointId = controlPointId,
            SortOrder = 1,
            IsRequired = false,
            IsChecked = false
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var controlPoint = await context.JobReportInstallationControlPoints.SingleAsync();
        controlPoint.IsChecked = true;
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Control point Trykprøvning changed", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        Assert.Contains(response.Changes, change => change.PropertyName == "ControlPoint" && change.After == "Trykprøvning");
        Assert.Contains(response.Changes, change => change.PropertyName == "IsChecked" && change.Before == "false" && change.After == "true");
    }


    [Fact]
    public async Task Job_report_status_change_audit_stays_on_job_report()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.Status).CurrentValue = "InReview";
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Status changed: 'Draft' → 'InReview'", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("Status", change.PropertyName);
        Assert.Equal("Status", change.DisplayName);
        Assert.Equal("Draft", change.Before);
        Assert.Equal("InReview", change.After);
    }

    [Fact]
    public async Task Job_report_closure_flag_add_audit_uses_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var closureFlagId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobClosureFlags.Add(new JobClosureFlagRow
        {
            Id = closureFlagId,
            NormalizedLabel = "finished",
            Label = "Færdigmeldt",
            IsActive = true,
            SortOrder = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.JobReportClosureFlags.Add(new JobReportClosureFlagRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            JobReportId = jobId,
            ClosureFlagId = closureFlagId,
            SortOrder = 1
        });
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Closure flag Færdigmeldt added", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("ClosureFlag", change.PropertyName);
        Assert.Equal("Afslutningsflag", change.DisplayName);
        Assert.Null(change.Before);
        Assert.Equal("Færdigmeldt", change.After);
    }

    [Fact]
    public async Task Job_report_closure_flag_remove_audit_uses_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var closureFlagId = Guid.NewGuid();
        var joinId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobClosureFlags.Add(new JobClosureFlagRow
        {
            Id = closureFlagId,
            NormalizedLabel = "finished",
            Label = "Færdigmeldt",
            IsActive = true,
            SortOrder = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobReportClosureFlags.Add(new JobReportClosureFlagRow
        {
            Id = joinId,
            OrganizationId = orgId,
            JobReportId = jobId,
            ClosureFlagId = closureFlagId,
            SortOrder = 1
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var flag = await context.JobReportClosureFlags.SingleAsync(f => f.Id == joinId);
        context.JobReportClosureFlags.Remove(flag);
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Closure flag Færdigmeldt removed", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("ClosureFlag", change.PropertyName);
        Assert.Equal("Færdigmeldt", change.Before);
        Assert.Null(change.After);
    }


    [Fact]
    public async Task Worksheet_add_audit_uses_user_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = userId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.Worksheets.Add(new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            JobId = jobId,
            UserId = userId,
            WorkDate = new DateTime(2026, 6, 11),
            HoursWorked = 7.5m,
            SleptOnJob = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Worksheet for Tech Tim added", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        Assert.Contains(response.Changes, change => change.PropertyName == "Report" && change.DisplayName == "Sag" && change.After == "JOB-1");
        Assert.Contains(response.Changes, change => change.PropertyName == "AssignedUser" && change.DisplayName == "Tildelt bruger" && change.After == "Tech Tim");
        Assert.Contains(response.Changes, change => change.PropertyName == "WorkDate" && change.DisplayName == "Arbejdsdato" && change.After == "11. juni 2026");
        Assert.Contains(response.Changes, change => change.PropertyName == "HoursWorked" && change.DisplayName == "Timer" && change.After == "7.5");
        Assert.Contains(response.Changes, change => change.PropertyName == "SleptOnJob" && change.DisplayName == "Overnatning" && change.After == "true");
        Assert.DoesNotContain(response.Changes, change => change.PropertyName == "JobId");
        Assert.DoesNotContain(response.Changes, change => change.PropertyName == "UserId");
    }

    [Fact]
    public async Task Worksheet_remove_audit_uses_user_display_name()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var worksheetId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = userId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.Worksheets.Add(new WorksheetRow
        {
            Id = worksheetId,
            OrganizationId = orgId,
            JobId = jobId,
            UserId = userId,
            WorkDate = new DateTime(2026, 6, 11),
            HoursWorked = 7.5m,
            SleptOnJob = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var worksheet = await context.Worksheets.SingleAsync(w => w.Id == worksheetId);
        context.Worksheets.Remove(worksheet);
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Worksheet for Tech Tim removed", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        Assert.Contains(response.Changes, change => change.PropertyName == "AssignedUser" && change.Before == "Tech Tim" && change.After is null);
        Assert.DoesNotContain(response.Changes, change => change.PropertyName == "UserId");
    }

    [Fact]
    public async Task Worksheet_edit_audit_tracks_hours_slept_on_job_and_assigned_user()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var worksheetId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = firstUserId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" },
            new UserDataRow { Id = secondUserId, OrganizationId = orgId, DisplayName = "Tech Tina", Email = "tina@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.Worksheets.Add(new WorksheetRow
        {
            Id = worksheetId,
            OrganizationId = orgId,
            JobId = jobId,
            UserId = firstUserId,
            WorkDate = new DateTime(2026, 6, 11),
            HoursWorked = 7.5m,
            SleptOnJob = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var worksheet = await context.Worksheets.SingleAsync(w => w.Id == worksheetId);
        worksheet.UserId = secondUserId;
        worksheet.HoursWorked = 8.25m;
        worksheet.SleptOnJob = true;
        await context.SaveChangesAsync();

        var audit = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("HoursWorked, SleptOnJob, AssignedUser changed", audit.Summary);

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        Assert.Contains(response.Changes, change => change.PropertyName == "AssignedUser" && change.Before == "Tech Tim" && change.After == "Tech Tina");
        Assert.Contains(response.Changes, change => change.PropertyName == "HoursWorked" && change.Before == "7.5" && change.After == "8.25");
        Assert.Contains(response.Changes, change => change.PropertyName == "SleptOnJob" && change.Before == "false" && change.After == "true");
        Assert.DoesNotContain(response.Changes, change => change.PropertyName == "UserId");
    }


    [Fact]
    public async Task Installation_category_and_control_point_add_remove_are_audited()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var installationTypeId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var categoryJoinId = Guid.NewGuid();
        var controlPointId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.InstallationTypeDefinitions.Add(new InstallationTypeDefinitionRow { Id = installationTypeId, OrganizationId = orgId, Name = "Gasinstallation", SortOrder = 1 });
        context.ControlCategoryRow.Add(new ControlCategoryRow { Id = categoryId, Name = "Modtagekontrol", SortOrder = 1 });
        context.ControlPointRow.Add(new ControlPointRow { Id = controlPointId, Name = "Trykprøvning", SortOrder = 1 });
        context.JobReportInstallations.Add(new JobReportInstallationRow
        {
            Id = installationId,
            OrganizationId = orgId,
            JobReportId = jobId,
            InstallationTypeDefinitionId = installationTypeId,
            SortOrder = 1
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.JobReportInstallationCategories.Add(new JobReportInstallationCategoryRow
        {
            Id = categoryJoinId,
            JobReportInstallationId = installationId,
            ControlCategoryId = categoryId,
            SortOrder = 1,
            IsIrrelevant = false
        });
        context.JobReportInstallationControlPoints.Add(new JobReportInstallationControlPointRow
        {
            JobReportInstallationCategoryId = categoryJoinId,
            ControlPointId = controlPointId,
            SortOrder = 1,
            IsRequired = true,
            IsChecked = false
        });
        await context.SaveChangesAsync();

        var addEvents = await context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId).ToListAsync();
        Assert.Contains(addEvents, e => e.Summary == "Category Modtagekontrol added");
        Assert.Contains(addEvents, e => e.Summary == "Control point Trykprøvning added");

        context.JobEvents.RemoveRange(context.JobEvents);
        await context.SaveChangesAsync();

        var controlPoint = await context.JobReportInstallationControlPoints.SingleAsync(cp => cp.JobReportInstallationCategoryId == categoryJoinId && cp.ControlPointId == controlPointId);
        context.JobReportInstallationControlPoints.Remove(controlPoint);
        var category = await context.JobReportInstallationCategories.SingleAsync(c => c.Id == categoryJoinId);
        context.JobReportInstallationCategories.Remove(category);
        await context.SaveChangesAsync();

        var removeEvents = await context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId).ToListAsync();
        Assert.Contains(removeEvents, e => e.Summary == "Category Modtagekontrol removed");
        Assert.Contains(removeEvents, e => e.Summary == "Control point Trykprøvning removed");
    }


    [Fact]
    public async Task Draft_job_changes_are_not_audited_before_first_in_review_transition()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.TaskDescription).CurrentValue = "Kladdearbejde";
        await context.SaveChangesAsync();

        Assert.Empty(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
    }

    [Fact]
    public async Task First_transition_to_in_review_activates_job_history()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.Status).CurrentValue = "InReview";
        await context.SaveChangesAsync();

        var transition = Assert.Single(context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId));
        Assert.Equal("Status changed: 'Draft' → 'InReview'", transition.Summary);

        report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.TaskDescription).CurrentValue = "Efter review";
        await context.SaveChangesAsync();

        var events = await context.JobEvents.AsNoTracking().Where(e => e.ReportId == jobId).OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Summary == "TaskDescription changed: '(empty)' → 'Efter review'");
    }

    [Fact]
    public async Task Draft_job_with_existing_history_keeps_capturing_events()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobEvents.Add(new JobEventRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ReportId = jobId,
            ActorId = actorId,
            EventType = "modified",
            Summary = "Status changed: 'Draft' → 'InReview'",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.TaskDescription).CurrentValue = "Efter tidligere review";
        await context.SaveChangesAsync();

        Assert.Equal(2, context.JobEvents.AsNoTracking().Count(e => e.ReportId == jobId));
    }


    [Fact]
    public async Task Status_change_to_draft_and_related_changes_in_same_save_logs_all_job_events()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var closureFlagId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = assignedUserId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "InReview",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobClosureFlags.Add(new JobClosureFlagRow
        {
            Id = closureFlagId,
            NormalizedLabel = "finished",
            Label = "Færdigmeldt",
            IsActive = true,
            SortOrder = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.Status).CurrentValue = "Draft";
        context.JobAssignments.Add(new JobAssignmentRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ReportId = jobId,
            UserId = assignedUserId,
            AssignedByUserId = actorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        context.JobReportClosureFlags.Add(new JobReportClosureFlagRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            JobReportId = jobId,
            ClosureFlagId = closureFlagId,
            SortOrder = 1
        });
        await context.SaveChangesAsync();

        var summaries = await context.JobEvents.AsNoTracking()
            .Where(e => e.ReportId == jobId)
            .Select(e => e.Summary)
            .ToListAsync();

        Assert.Equal(3, summaries.Count);
        Assert.Contains("Status changed: 'InReview' → 'Draft'", summaries);
        Assert.Contains("Tech Tim assigned", summaries);
        Assert.Contains("Closure flag Færdigmeldt added", summaries);
    }

    [Fact]
    public async Task First_transition_to_in_review_and_related_changes_in_same_save_logs_all_job_events()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var closureFlagId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.AddRange(
            new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" },
            new UserDataRow { Id = assignedUserId, OrganizationId = orgId, DisplayName = "Tech Tim", Email = "tim@example.test", Role = "User" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.JobClosureFlags.Add(new JobClosureFlagRow
        {
            Id = closureFlagId,
            NormalizedLabel = "finished",
            Label = "Færdigmeldt",
            IsActive = true,
            SortOrder = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var report = await context.JobReports.SingleAsync(r => r.Id == jobId);
        context.Entry(report).Property(e => e.Status).CurrentValue = "InReview";
        context.JobAssignments.Add(new JobAssignmentRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            ReportId = jobId,
            UserId = assignedUserId,
            AssignedByUserId = actorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        context.JobReportClosureFlags.Add(new JobReportClosureFlagRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            JobReportId = jobId,
            ClosureFlagId = closureFlagId,
            SortOrder = 1
        });
        await context.SaveChangesAsync();

        var summaries = await context.JobEvents.AsNoTracking()
            .Where(e => e.ReportId == jobId)
            .Select(e => e.Summary)
            .ToListAsync();

        Assert.Equal(3, summaries.Count);
        Assert.Contains("Status changed: 'Draft' → 'InReview'", summaries);
        Assert.Contains("Tech Tim assigned", summaries);
        Assert.Contains("Closure flag Færdigmeldt added", summaries);
    }


    [Fact]
    public async Task Job_repository_update_after_status_activation_logs_real_work_events_without_churn()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var installationTypeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var controlPointId = Guid.NewGuid();
        var closureFlagId = Guid.NewGuid();
        await using var context = CreateContext(orgId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = orgId, Name = "Acme", Cvr = "12345678" });
        context.Users.Add(new UserDataRow { Id = actorId, OrganizationId = orgId, DisplayName = "Planner Pia", Email = "pia@example.test", Role = "Admin" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = orgId,
            ReportNumber = "JOB-1",
            Status = "Draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.InstallationTypeDefinitions.Add(new InstallationTypeDefinitionRow
        {
            Id = installationTypeId,
            OrganizationId = orgId,
            Name = "Gasinstallation",
            SortOrder = 1
        });
        context.ControlCategoryRow.Add(new ControlCategoryRow { Id = categoryId, Name = "Modtagekontrol", SortOrder = 1 });
        context.ControlPointRow.Add(new ControlPointRow { Id = controlPointId, Name = "Trykprøvning", SortOrder = 1 });
        context.InstallationTypeDefinitionMappings.Add(new InstallationTypeDefinitionMappingRow
        {
            InstallationTypeDefinitionId = installationTypeId,
            ControlCategoryId = categoryId,
            ControlPointId = controlPointId,
            SortOrder = 1,
            IsRequired = true
        });
        context.JobClosureFlags.Add(new JobClosureFlagRow
        {
            Id = closureFlagId,
            NormalizedLabel = "finished",
            Label = "Færdigmeldt",
            IsActive = true,
            SortOrder = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var repository = CreateJobRepository(context, actorId, orgId);
        await repository.TransitionAsync(jobId, orgId, JobStatus.InReview, actorId, CancellationToken.None);

        var request = new UpdateJobRequest(
            Customer: null,
            ReportNumber: null,
            Work: new CreateJobWorkRequest(
                InstallationTypes:
                [
                    new CreateInstallationTypeRequest(installationTypeId,
                    [
                        new CreateInstallationTypeCategoryRequest(categoryId,
                        [
                            new CreateInstallationTypeControlPointRequest(controlPointId, SortOrder: 1, IsRequired: true, IsChecked: true)
                        ],
                        IsIrrelevant: false)
                    ])
                ],
                WorkKind: null,
                CustomWorkKind: null,
                ClosureFlags: ["finished"],
                Remarks: null),
            Observations: new CreateJobObservationRequest(
                ReportDate: null,
                TaskDescription: "Efter review",
                CustomerObservations: null,
                TechnicalObservations: null));

        await repository.UpdateAsync(jobId, orgId, request, CancellationToken.None);

        var summaries = await context.JobEvents.AsNoTracking()
            .Where(e => e.ReportId == jobId)
            .Select(e => e.Summary)
            .ToListAsync();

        Assert.Equal(6, summaries.Count);
        Assert.Contains("Status changed: 'Draft' → 'InReview'", summaries);
        Assert.Contains("TaskDescription changed: '(empty)' → 'Efter review'", summaries);
        Assert.Contains("Installation type Gasinstallation added", summaries);
        Assert.Contains("Category Modtagekontrol added", summaries);
        Assert.Contains("Control point Trykprøvning added", summaries);
        Assert.Contains("Closure flag Færdigmeldt added", summaries);

        await repository.UpdateAsync(jobId, orgId, request, CancellationToken.None);

        var afterNoopUpdateCount = await context.JobEvents.AsNoTracking().CountAsync(e => e.ReportId == jobId);
        Assert.Equal(6, afterNoopUpdateCount);
    }


    [Fact]
    public void History_response_uses_danish_labels_and_never_exposes_raw_guid_values()
    {
        var rawGuid = Guid.NewGuid().ToString();
        var audit = new JobEventRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ReportId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            EventType = "modified",
            Summary = "CustomerId changed",
            BeforeJson = $"{{\"CustomerId\":\"{rawGuid}\",\"TaskDescription\":\"Old task\"}}",
            AfterJson = $"{{\"CustomerId\":\"{Guid.NewGuid()}\",\"TaskDescription\":\"New task\"}}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");

        Assert.Equal("Felter ændret: Kunde, Opgavebeskrivelse", response.Summary);
        Assert.DoesNotContain("CustomerId", response.Summary);
        Assert.DoesNotContain(rawGuid, response.Summary);
        Assert.DoesNotContain(response.Changes, change => change.DisplayName == change.PropertyName && change.PropertyName == "CustomerId");
        Assert.Contains(response.Changes, change => change.DisplayName == "Kunde" && change.Before == "Ikke vist" && change.After == "Ikke vist");
        Assert.Contains(response.Changes, change => change.DisplayName == "Opgavebeskrivelse" && change.Before == "Old task" && change.After == "New task");
    }

    [Fact]
    public void History_response_formats_dates_as_danish_long_dates_without_time()
    {
        var audit = new JobEventRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ReportId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            EventType = "modified",
            Summary = "WorkDate changed",
            BeforeJson = "{\"WorkDate\":\"2025-12-31T23:45:00Z\"}",
            AfterJson = "{\"WorkDate\":\"2026-01-01T08:15:30\"}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var response = JobReportMapper.ToHistoryResponse(audit, "Planner Pia");
        var change = Assert.Single(response.Changes);
        Assert.Equal("Arbejdsdato", change.DisplayName);
        Assert.Equal("31. december 2025", change.Before);
        Assert.Equal("1. januar 2026", change.After);
        Assert.Equal("Arbejdsdato ændret: '31. december 2025' → '1. januar 2026'", response.Summary);
        Assert.DoesNotContain("08:15", response.Summary);
        Assert.DoesNotContain("23:45", response.Summary);
    }

    private static void SeedReportsWithLinks(
        SqlDbContext context,
        Guid organizationId,
        IReadOnlyList<(Guid Id, string ReportNumber)> reports,
        IReadOnlyList<JobReportLinkRow> links)
    {
        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = organizationId, Name = "Acme", Cvr = "12345678" });
        foreach (var (id, reportNumber) in reports)
        {
            context.JobReports.Add(new JobReportRow
            {
                Id = id,
                OrganizationId = organizationId,
                ReportNumber = reportNumber,
                Status = "InReview",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        context.JobReportLinks.AddRange(links);
        context.SaveChanges();
        context.IsSeeding = false;
    }


    private static EfJobRepository CreateJobRepository(SqlDbContext context, Guid userId, Guid organizationId)
    {
        var retryPolicy = new NoRetryPolicy();
        var currentUser = new TestCurrentUserContext(userId, organizationId);
        var worksheetRepository = new EfWorksheetRepository(context, currentUser, retryPolicy);
        var assignmentRepository = new EfAssignmentRepository(context, retryPolicy, currentUser, worksheetRepository);
        var linkRepository = new EfJobLinkRepository(context, retryPolicy);
        return new EfJobRepository(
            context,
            retryPolicy,
            new EfCustomerRepository(context),
            assignmentRepository,
            linkRepository,
            worksheetRepository);
    }

    private static SqlDbContext CreateContext(Guid organizationId, Guid userId)
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new AuditInterceptor(new TestCurrentUserContext(userId, organizationId)))
            .Options;

        return new SqlDbContext(options);
    }

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => "Admin";
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
