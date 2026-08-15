using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class AuditorDataScopeTests
{
    [Fact]
    public async Task ReferenceDataService_filters_installation_types_for_auditor()
    {
        var data = CreateReferenceData("Gas", "Vand", "Afløb", "Ventilation");
        var service = new ReferenceDataService(
            new StubReferenceDataRepository(data),
            new StubCurrentUserContext(Guid.NewGuid(), Guid.NewGuid(), Roles.Auditor));

        var result = await service.GetAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [AuditorDataScope.WaterInstallationType, AuditorDataScope.DrainageInstallationType],
            result.Value.InstallationTypes.Select(type => type.Name));
    }

    [Fact]
    public void Job_list_filter_hides_disallowed_only_jobs_and_redacts_mixed_jobs()
    {
        var hidden = AuditorDataScope.Filter(CreateListItem("Gas", "Ventilation"));
        var mixed = AuditorDataScope.Filter(CreateListItem("Varme", "Vand", "Afløb"));

        Assert.Null(hidden);
        Assert.NotNull(mixed);
        Assert.Equal(
            [AuditorDataScope.WaterInstallationType, AuditorDataScope.DrainageInstallationType],
            mixed!.InstallationTypes);
    }

    [Fact]
    public void Job_detail_filter_hides_disallowed_only_jobs_and_redacts_mixed_jobs()
    {
        var hidden = AuditorDataScope.Filter(CreateSummary("Gas", "Varme"));
        var mixed = AuditorDataScope.Filter(CreateSummary("Ventilation", "Vand", "Afløb"));

        Assert.Null(hidden);
        Assert.NotNull(mixed);
        Assert.Equal(
            [AuditorDataScope.WaterInstallationType, AuditorDataScope.DrainageInstallationType],
            mixed!.Work.InstallationTypes.Select(type => type.Name));
    }

    [Fact]
    public void History_filter_keeps_allowed_installation_and_general_events_but_removes_hidden_or_ambiguous_scope_data()
    {
        var now = DateTimeOffset.UtcNow;
        var general = CreateHistoryEvent(now, new PropertyChange("Status", "Status", "Til gennemsyn", "Godkendt"));
        var allowed = CreateHistoryEvent(now.AddSeconds(1), new PropertyChange("InstallationType", "Anlægstype", null, "Vand"));
        var hidden = CreateHistoryEvent(now.AddSeconds(2), new PropertyChange("InstallationType", "Anlægstype", null, "Varme"));
        var ambiguousControlPoint = CreateHistoryEvent(now.AddSeconds(3), new PropertyChange("IsChecked", "Afkrydset", "Nej", "Ja"));
        var historicalLink = CreateHistoryEvent(now.AddSeconds(4), new PropertyChange("LinkedReport", "Relateret sag", null, "Sag 0042"));

        var filtered = AuditorDataScope.Filter(
            new[] { general, allowed, hidden, ambiguousControlPoint, historicalLink });

        Assert.Equal([general.Id, allowed.Id], filtered.Select(item => item.Id));
    }

    [Fact]
    public void History_filter_keeps_scope_change_but_redacts_internal_admin_reason()
    {
        var now = DateTimeOffset.UtcNow;
        var scopeChange = new JobHistoryResponse(
            Guid.NewGuid(),
            null,
            null,
            "modified",
            "Felter ændret: Indgår i auditørvisning, Begrundelse for audit-scope",
            new PropertyChange[]
            {
                new("IsInAuditorScope", "Indgår i auditørvisning", "Nej", "Ja"),
                new("AuditorScopeReason", "Begrundelse for audit-scope", "Intern ledelsesopgave", null)
            },
            now);

        var filtered = AuditorDataScope.Filter([scopeChange]);

        var visible = Assert.Single(filtered);
        Assert.Equal("Auditørvisning ændret", visible.Summary);
        var change = Assert.Single(visible.Changes);
        Assert.Equal("IsInAuditorScope", change.PropertyName);
        Assert.DoesNotContain("Intern ledelsesopgave", visible.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void History_filter_drops_reason_only_scope_event()
    {
        var reasonOnly = CreateHistoryEvent(
            DateTimeOffset.UtcNow,
            new PropertyChange("AuditorScopeReason", "Begrundelse for audit-scope", "Før", "Efter"));

        var filtered = AuditorDataScope.Filter([reasonOnly]);

        Assert.Empty(filtered);
    }

    [Theory]
    [InlineData("Auditor", true)]
    [InlineData("auditor", true)]
    [InlineData("Admin", false)]
    [InlineData("User", false)]
    public void Scope_applies_only_to_auditor_role(string role, bool expected)
    {
        Assert.Equal(expected, AuditorDataScope.AppliesTo(role));
    }

    private static ReferenceDataResponse CreateReferenceData(params string[] names) =>
        new(
            names.Select((name, index) => new InstallationTypeDefinitionResponse(
                Guid.NewGuid(),
                name,
                index + 1,
                Array.Empty<DefinitionCategoryResponse>())).ToArray(),
            Array.Empty<WorkKindResponse>(),
            Array.Empty<ClosureFlagResponse>());

    private static JobListItemResponse CreateListItem(params string[] installationTypes) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "0001",
            JobStatus.Approved,
            null,
            JobType.KLS,
            null,
            null,
            installationTypes,
            Array.Empty<AssignedUserResponse>(),
            false,
            null,
            DateTimeOffset.UtcNow,
            true,
            false);

    private static JobReportSummaryResponse CreateSummary(params string[] installationTypes)
    {
        var now = DateTimeOffset.UtcNow;
        var types = installationTypes.Select((name, index) => new InstallationTypeResponse(
            Guid.NewGuid(),
            name,
            index + 1,
            Array.Empty<InstallationTypeCategoryResponse>())).ToArray();

        return new JobReportSummaryResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Test organization",
            "12345678",
            "0001",
            JobStatus.Approved,
            null,
            new CustomerSnapshotResponse(null, null, null, null, null),
            null,
            null,
            null,
            JobType.KLS.ToString(),
            new JobReportSummaryWorkResponse(
                null,
                types,
                Array.Empty<JobReportSummaryClosureFlagResponse>(),
                null),
            new JobReportSummaryObservationResponse(null, null, null),
            Array.Empty<ControlInstallationTypeResponse>(),
            Array.Empty<JobLinkInfoResponse>(),
            now,
            now,
            null,
            Array.Empty<AssignedUserResponse>(),
            Array.Empty<WorksheetResponse>(),
            null,
            null,
            false,
            null);
    }

    private static JobHistoryResponse CreateHistoryEvent(DateTimeOffset createdAt, PropertyChange change) =>
        new(
            Guid.NewGuid(),
            null,
            null,
            "modified",
            "Test",
            new[] { change },
            createdAt);

    private sealed record StubCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class StubReferenceDataRepository(ReferenceDataResponse data) : IReferenceDataRepository
    {
        public Task<ReferenceDataResponse> GetAsync(Guid? organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(data);
    }
}
