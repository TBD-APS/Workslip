using Ardalis.Result;
using Workslip.Application.Auditing;
using Workslip.Application.Auth;
using Workslip.Application.Images;
using Workslip.Domain;

namespace Workslip.Tests.Auditing;

public sealed class AuditorServiceAuthorizationTests
{
    private static readonly Guid AuditorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ControlOrganizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetOrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid JobId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task ListReportsAsync_WithoutActiveAssignment_FailsClosedBeforeTargetQuery()
    {
        var repository = new FakeAuditorRepository();
        var service = new AuditorService(
            repository,
            new NullImageStorage(),
            new FakeCurrentUserContext(AuditorUserId, ControlOrganizationId, Roles.Auditor));

        var result = await service.ListReportsAsync(
            TargetOrganizationId,
            search: null,
            installationType: null,
            limit: null,
            offset: null,
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetActiveAssignmentCalls);
        Assert.Equal(0, repository.ListReportsCalls);
    }

    [Fact]
    public async Task GetReportAsync_AssignmentDoesNotBypassPerJobAuditorScope()
    {
        var repository = new FakeAuditorRepository
        {
            ActiveAssignment = Grant(),
            VisibleJob = false,
        };
        var service = new AuditorService(
            repository,
            new NullImageStorage(),
            new FakeCurrentUserContext(AuditorUserId, ControlOrganizationId, Roles.Auditor));

        var result = await service.GetReportAsync(TargetOrganizationId, JobId, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetActiveAssignmentCalls);
        Assert.Equal(1, repository.IsVisibleJobCalls);
        Assert.Equal(0, repository.GetReportCalls);
    }

    [Fact]
    public async Task ListReportsAsync_NonAuditorCannotUseAuditorAssignmentPath()
    {
        var repository = new FakeAuditorRepository { ActiveAssignment = Grant() };
        var service = new AuditorService(
            repository,
            new NullImageStorage(),
            new FakeCurrentUserContext(AuditorUserId, ControlOrganizationId, Roles.Admin));

        var result = await service.ListReportsAsync(
            TargetOrganizationId,
            search: null,
            installationType: null,
            limit: null,
            offset: null,
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.GetActiveAssignmentCalls);
        Assert.Equal(0, repository.ListReportsCalls);
    }

    [Fact]
    public async Task CreateAssignmentAsync_RejectsControlOrganizationAsTarget()
    {
        var repository = new FakeAuditorRepository
        {
            AuditorUser = new AuditorUserSubject(
                AuditorUserId,
                ControlOrganizationId,
                "Teknik Q Auditor",
                "auditor@example.test",
                Roles.Auditor),
            OrganizationExists = true,
        };
        var service = new AuditorService(
            repository,
            new NullImageStorage(),
            new FakeCurrentUserContext(Guid.NewGuid(), Guid.NewGuid(), Roles.Superadmin));

        var result = await service.CreateAssignmentAsync(
            new CreateAuditorAssignmentRequest(
                AuditorUserId,
                ControlOrganizationId,
                "VVS",
                DateTimeOffset.UtcNow,
                null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, repository.CreateAssignmentCalls);
    }

    [Fact]
    public async Task AddCompanyEvidenceAsync_CannotCrossCurrentOrganizationBoundary()
    {
        var repository = new FakeAuditorRepository { FindingTarget = null };
        var companyUserId = Guid.NewGuid();
        var companyOrganizationId = Guid.NewGuid();
        var service = new AuditorService(
            repository,
            new NullImageStorage(),
            new FakeCurrentUserContext(companyUserId, companyOrganizationId, Roles.Admin));

        var result = await service.AddCompanyEvidenceAsync(
            Guid.NewGuid(),
            new AddAuditorFindingEvidenceRequest("Dokumentation for korrigerende handling."),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(companyOrganizationId, repository.LastFindingTargetOrganizationId);
        Assert.Equal(0, repository.AddEvidenceCalls);
    }

    private static AuditorAssignmentGrant Grant() => new(
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
        AuditorUserId,
        ControlOrganizationId,
        TargetOrganizationId,
        "VVS",
        DateTimeOffset.UtcNow.AddDays(-1),
        null);

    private sealed class FakeCurrentUserContext(Guid? userId, Guid? organizationId, string? role) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = role;
    }

    private sealed class FakeAuditorRepository : IAuditorRepository
    {
        public AuditorAssignmentGrant? ActiveAssignment { get; init; }
        public bool VisibleJob { get; init; }
        public AuditorUserSubject? AuditorUser { get; init; }
        public bool OrganizationExists { get; init; }
        public AuditorFindingTarget? FindingTarget { get; init; }

        public int GetActiveAssignmentCalls { get; private set; }
        public int ListReportsCalls { get; private set; }
        public int IsVisibleJobCalls { get; private set; }
        public int GetReportCalls { get; private set; }
        public int CreateAssignmentCalls { get; private set; }
        public int AddEvidenceCalls { get; private set; }
        public Guid? LastFindingTargetOrganizationId { get; private set; }

        public Task<IReadOnlyList<AuditorOrganizationSummaryResponse>> ListOrganizationsAsync(Guid auditorUserId, Guid controlOrganizationId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditorOrganizationSummaryResponse>>([]);

        public Task<AuditorAssignmentGrant?> GetActiveAssignmentAsync(Guid auditorUserId, Guid controlOrganizationId, Guid targetOrganizationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            GetActiveAssignmentCalls++;
            return Task.FromResult(ActiveAssignment);
        }

        public Task<bool> IsVisibleJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
        {
            IsVisibleJobCalls++;
            return Task.FromResult(VisibleJob);
        }

        public Task<AuditorReportListResponse> ListReportsAsync(Guid organizationId, string? search, string? installationType, int limit, int offset, CancellationToken cancellationToken)
        {
            ListReportsCalls++;
            return Task.FromResult(new AuditorReportListResponse([], 0));
        }

        public Task<AuditorReportDetailResponse?> GetReportAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
        {
            GetReportCalls++;
            return Task.FromResult<AuditorReportDetailResponse?>(null);
        }

        public Task<IReadOnlyList<AuditorFindingResponse>> ListFindingsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditorFindingResponse>>([]);

        public Task<IReadOnlyList<AuditorEventResponse>> ListEventsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditorEventResponse>>([]);

        public Task RecordReportOpenedAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AuditorFindingResponse?> CreateFindingAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid actorUserId, CreateAuditorFindingCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<AuditorFindingResponse?>(null);

        public Task<bool> FindingBelongsToJobAsync(Guid findingId, Guid organizationId, Guid jobId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<AuditorFindingResponse?> UpdateFindingAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid findingId, Guid actorUserId, UpdateAuditorFindingCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<AuditorFindingResponse?>(null);

        public Task<AuditorFindingTarget?> GetFindingTargetAsync(Guid findingId, Guid organizationId, CancellationToken cancellationToken)
        {
            LastFindingTargetOrganizationId = organizationId;
            return Task.FromResult(FindingTarget);
        }

        public Task<AuditorFindingResponse?> AddCompanyEvidenceAsync(AuditorFindingTarget target, Guid findingId, Guid actorUserId, string evidence, DateTimeOffset now, CancellationToken cancellationToken)
        {
            AddEvidenceCalls++;
            return Task.FromResult<AuditorFindingResponse?>(null);
        }

        public Task<IReadOnlyList<AuditorAssignmentAdminResponse>> ListAssignmentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditorAssignmentAdminResponse>>([]);

        public Task<AuditorUserSubject?> GetAuditorUserAsync(Guid auditorUserId, CancellationToken cancellationToken) => Task.FromResult(AuditorUser);
        public Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(OrganizationExists);
        public Task<bool> ActiveAssignmentExistsAsync(Guid auditorUserId, Guid targetOrganizationId, string authorizationArea, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<AuditorAssignmentAdminResponse?> CreateAssignmentAsync(Guid actorUserId, AuditorUserSubject auditor, CreateAuditorAssignmentCommand command, CancellationToken cancellationToken)
        {
            CreateAssignmentCalls++;
            return Task.FromResult<AuditorAssignmentAdminResponse?>(null);
        }

        public Task<AuditorAssignmentTarget?> GetAssignmentTargetAsync(Guid assignmentId, CancellationToken cancellationToken) => Task.FromResult<AuditorAssignmentTarget?>(null);
        public Task<AuditorAssignmentAdminResponse?> UpdateAssignmentAsync(Guid assignmentId, Guid actorUserId, bool isActive, DateTimeOffset? activeUntil, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<AuditorAssignmentAdminResponse?>(null);
    }

    private sealed class NullImageStorage : IImageStorage
    {
        public Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ImageInfoResponse>>([]);
        public Task<ImageInfoResponse> UploadJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageFileResponse?> GetJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) => Task.FromResult<ImageFileResponse?>(null);
        public Task DeleteJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UploadProfileImageAsync(Guid organizationId, Guid userId, Stream content, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImageFileResponse?> GetProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<ImageFileResponse?>(null);
        public Task DeleteProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
