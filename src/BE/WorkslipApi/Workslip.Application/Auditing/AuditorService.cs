using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Images;
using Workslip.Domain;

namespace Workslip.Application.Auditing;

public sealed class AuditorService(
    IAuditorRepository repository,
    IImageStorage imageStorage,
    ICurrentUserContext currentUser) : IAuditorService
{
    private const string AuthorizationAreaVvs = "VVS";
    private const string FindingOpen = "Open";
    private const string FindingAwaitingEvidence = "AwaitingEvidence";
    private const string FindingReadyForVerification = "ReadyForVerification";
    private const string FindingClosed = "Closed";

    private static readonly string[] AllowedInstallationTypes = ["Vand", "Afløb"];
    private static readonly HashSet<string> FindingCategories = new(StringComparer.OrdinalIgnoreCase) { "A", "An", "Anb", "IR" };
    private static readonly HashSet<string> FindingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        FindingOpen,
        FindingAwaitingEvidence,
        FindingReadyForVerification,
        FindingClosed
    };

    public async Task<Result<IReadOnlyList<AuditorOrganizationSummaryResponse>>> ListOrganizationsAsync(CancellationToken cancellationToken)
    {
        if (!TryGetAuditorActor(out var userId, out var controlOrganizationId))
            return Result<IReadOnlyList<AuditorOrganizationSummaryResponse>>.Unauthorized();

        var rows = await repository.ListOrganizationsAsync(userId, controlOrganizationId, DateTimeOffset.UtcNow, cancellationToken);
        return Result<IReadOnlyList<AuditorOrganizationSummaryResponse>>.Success(rows);
    }

    public async Task<Result<AuditorReportListResponse>> ListReportsAsync(
        Guid organizationId,
        string? search,
        string? installationType,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var access = await RequireAssignmentAsync(organizationId, cancellationToken);
        if (!access.IsSuccess)
            return Result<AuditorReportListResponse>.NotFound();

        var normalizedInstallationType = NormalizeInstallationFilter(installationType);
        if (installationType is not null && normalizedInstallationType is null)
            return Invalid<AuditorReportListResponse>(nameof(installationType), "Anlægstype skal være Vand eller Afløb.");

        var result = await repository.ListReportsAsync(
            organizationId,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            normalizedInstallationType,
            Math.Clamp(limit ?? 50, 1, 200),
            Math.Max(offset ?? 0, 0),
            cancellationToken);
        return Result<AuditorReportListResponse>.Success(result);
    }

    public async Task<Result<AuditorReportDetailResponse>> GetReportAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        var access = await RequireAssignmentAsync(organizationId, cancellationToken);
        if (!access.IsSuccess || access.Value is null || !await repository.IsVisibleJobAsync(organizationId, jobId, cancellationToken))
            return Result<AuditorReportDetailResponse>.NotFound();

        var report = await repository.GetReportAsync(organizationId, jobId, cancellationToken);
        if (report is null)
            return Result<AuditorReportDetailResponse>.NotFound();

        var findings = await repository.ListFindingsAsync(organizationId, jobId, cancellationToken);
        var events = await repository.ListEventsAsync(organizationId, jobId, cancellationToken);
        var images = await imageStorage.ListJobImagesAsync(organizationId, jobId, cancellationToken);

        await repository.RecordReportOpenedAsync(access.Value.Id, organizationId, jobId, currentUser.UserId!.Value, cancellationToken);

        return Result<AuditorReportDetailResponse>.Success(report with
        {
            Findings = findings,
            AuditEvents = events,
            Images = images
        });
    }

    public async Task<Result<IReadOnlyList<ImageInfoResponse>>> ListImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await CanAccessJobAsync(organizationId, jobId, cancellationToken))
            return Result<IReadOnlyList<ImageInfoResponse>>.NotFound();

        var images = await imageStorage.ListJobImagesAsync(organizationId, jobId, cancellationToken);
        return Result<IReadOnlyList<ImageInfoResponse>>.Success(images);
    }

    public async Task<Result<ImageFileResponse>> GetImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken)
    {
        if (!await CanAccessJobAsync(organizationId, jobId, cancellationToken))
            return Result<ImageFileResponse>.NotFound();

        var image = await imageStorage.GetJobImageAsync(organizationId, jobId, imageId, cancellationToken);
        return image is null ? Result<ImageFileResponse>.NotFound() : Result<ImageFileResponse>.Success(image);
    }

    public async Task<Result<AuditorFindingResponse>> CreateFindingAsync(
        Guid organizationId,
        Guid jobId,
        CreateAuditorFindingRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateFinding(request.Category, request.Description, request.Reference);
        if (validation is not null)
            return Result<AuditorFindingResponse>.Invalid(validation);

        var access = await RequireAssignmentAsync(organizationId, cancellationToken);
        if (!access.IsSuccess || access.Value is null || !await repository.IsVisibleJobAsync(organizationId, jobId, cancellationToken))
            return Result<AuditorFindingResponse>.NotFound();

        var command = new CreateAuditorFindingCommand(
            NormalizeFindingCategory(request.Category),
            request.Description.Trim(),
            NormalizeOptionalText(request.Reference),
            request.DueAt,
            DateTimeOffset.UtcNow);
        var finding = await repository.CreateFindingAsync(
            access.Value.Id,
            organizationId,
            jobId,
            currentUser.UserId!.Value,
            command,
            cancellationToken);
        return finding is null ? Result<AuditorFindingResponse>.NotFound() : Result<AuditorFindingResponse>.Success(finding);
    }

    public async Task<Result<AuditorFindingResponse>> UpdateFindingAsync(
        Guid organizationId,
        Guid jobId,
        Guid findingId,
        UpdateAuditorFindingRequest request,
        CancellationToken cancellationToken)
    {
        if (!FindingStatuses.Contains(request.Status))
            return Invalid<AuditorFindingResponse>(nameof(request.Status), "Ukendt fundstatus.");
        if (request.Description is not null && string.IsNullOrWhiteSpace(request.Description))
            return Invalid<AuditorFindingResponse>(nameof(request.Description), "Beskrivelsen må ikke være tom.");
        if (request.Description?.Length > 4000)
            return Invalid<AuditorFindingResponse>(nameof(request.Description), "Beskrivelsen må højst være 4000 tegn.");

        var access = await RequireAssignmentAsync(organizationId, cancellationToken);
        if (!access.IsSuccess || access.Value is null
            || !await repository.IsVisibleJobAsync(organizationId, jobId, cancellationToken)
            || !await repository.FindingBelongsToJobAsync(findingId, organizationId, jobId, cancellationToken))
        {
            return Result<AuditorFindingResponse>.NotFound();
        }

        var finding = await repository.UpdateFindingAsync(
            access.Value.Id,
            organizationId,
            jobId,
            findingId,
            currentUser.UserId!.Value,
            new UpdateAuditorFindingCommand(
                NormalizeFindingStatus(request.Status),
                request.Description is null ? null : request.Description.Trim(),
                request.DueAt,
                DateTimeOffset.UtcNow),
            cancellationToken);
        return finding is null ? Result<AuditorFindingResponse>.NotFound() : Result<AuditorFindingResponse>.Success(finding);
    }

    public async Task<Result<AuditorFindingResponse>> AddCompanyEvidenceAsync(
        Guid findingId,
        AddAuditorFindingEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompanyActor(out var userId, out var organizationId))
            return Result<AuditorFindingResponse>.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Evidence))
            return Invalid<AuditorFindingResponse>(nameof(request.Evidence), "Dokumentation/svar er påkrævet.");
        if (request.Evidence.Length > 8000)
            return Invalid<AuditorFindingResponse>(nameof(request.Evidence), "Dokumentation/svar må højst være 8000 tegn.");

        var target = await repository.GetFindingTargetAsync(findingId, organizationId, cancellationToken);
        if (target is null)
            return Result<AuditorFindingResponse>.NotFound();
        if (string.Equals(target.Status, FindingClosed, StringComparison.OrdinalIgnoreCase))
            return Result<AuditorFindingResponse>.Conflict("finding_closed");

        var finding = await repository.AddCompanyEvidenceAsync(
            target,
            findingId,
            userId,
            request.Evidence.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);
        return finding is null ? Result<AuditorFindingResponse>.NotFound() : Result<AuditorFindingResponse>.Success(finding);
    }

    public async Task<Result<IReadOnlyList<AuditorAssignmentAdminResponse>>> ListAssignmentsAsync(CancellationToken cancellationToken)
    {
        if (!IsSuperAdmin())
            return Result<IReadOnlyList<AuditorAssignmentAdminResponse>>.Forbidden();
        var rows = await repository.ListAssignmentsAsync(cancellationToken);
        return Result<IReadOnlyList<AuditorAssignmentAdminResponse>>.Success(rows);
    }

    public async Task<Result<AuditorAssignmentAdminResponse>> CreateAssignmentAsync(CreateAuditorAssignmentRequest request, CancellationToken cancellationToken)
    {
        if (!IsSuperAdmin() || currentUser.UserId is not Guid actorUserId)
            return Result<AuditorAssignmentAdminResponse>.Forbidden();

        var activeFrom = request.ActiveFrom ?? DateTimeOffset.UtcNow;
        if (request.ActiveUntil is not null && request.ActiveUntil <= activeFrom)
            return Invalid<AuditorAssignmentAdminResponse>(nameof(request.ActiveUntil), "Slutdato skal ligge efter startdato.");
        if (!string.Equals(request.AuthorizationArea?.Trim(), AuthorizationAreaVvs, StringComparison.OrdinalIgnoreCase))
            return Invalid<AuditorAssignmentAdminResponse>(nameof(request.AuthorizationArea), "Auditor v1 understøtter fagområdet VVS.");

        var auditor = await repository.GetAuditorUserAsync(request.AuditorUserId, cancellationToken);
        if (auditor is null || !string.Equals(auditor.Role, Roles.Auditor, StringComparison.OrdinalIgnoreCase))
            return Invalid<AuditorAssignmentAdminResponse>(nameof(request.AuditorUserId), "Brugeren findes ikke eller har ikke auditor-rollen.");
        if (!await repository.OrganizationExistsAsync(request.TargetOrganizationId, cancellationToken))
            return Invalid<AuditorAssignmentAdminResponse>(nameof(request.TargetOrganizationId), "Målorganisationen findes ikke.");
        if (auditor.OrganizationId == request.TargetOrganizationId)
            return Invalid<AuditorAssignmentAdminResponse>(nameof(request.TargetOrganizationId), "Kontrolinstans og målorganisation skal være forskellige organisationer.");
        if (await repository.ActiveAssignmentExistsAsync(request.AuditorUserId, request.TargetOrganizationId, AuthorizationAreaVvs, DateTimeOffset.UtcNow, cancellationToken))
            return Result<AuditorAssignmentAdminResponse>.Conflict("active_assignment_exists");

        var now = DateTimeOffset.UtcNow;
        var result = await repository.CreateAssignmentAsync(
            actorUserId,
            auditor,
            new CreateAuditorAssignmentCommand(request.AuditorUserId, request.TargetOrganizationId, AuthorizationAreaVvs, activeFrom, request.ActiveUntil, now),
            cancellationToken);
        return result is null ? Result<AuditorAssignmentAdminResponse>.Error("assignment_create_failed") : Result<AuditorAssignmentAdminResponse>.Success(result);
    }

    public async Task<Result<AuditorAssignmentAdminResponse>> UpdateAssignmentAsync(Guid assignmentId, UpdateAuditorAssignmentRequest request, CancellationToken cancellationToken)
    {
        if (!IsSuperAdmin() || currentUser.UserId is not Guid actorUserId)
            return Result<AuditorAssignmentAdminResponse>.Forbidden();

        var target = await repository.GetAssignmentTargetAsync(assignmentId, cancellationToken);
        if (target is null)
            return Result<AuditorAssignmentAdminResponse>.NotFound();
        if (request.ActiveUntil is not null && request.ActiveUntil <= target.ActiveFrom)
            return Invalid<AuditorAssignmentAdminResponse>(nameof(request.ActiveUntil), "Slutdato skal ligge efter startdato.");

        var result = await repository.UpdateAssignmentAsync(
            assignmentId,
            actorUserId,
            request.IsActive,
            request.ActiveUntil,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return result is null ? Result<AuditorAssignmentAdminResponse>.NotFound() : Result<AuditorAssignmentAdminResponse>.Success(result);
    }

    private async Task<Result<AuditorAssignmentGrant>> RequireAssignmentAsync(Guid targetOrganizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuditorActor(out var userId, out var controlOrganizationId))
            return Result<AuditorAssignmentGrant>.Unauthorized();

        var assignment = await repository.GetActiveAssignmentAsync(
            userId,
            controlOrganizationId,
            targetOrganizationId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return assignment is null ? Result<AuditorAssignmentGrant>.NotFound() : Result<AuditorAssignmentGrant>.Success(assignment);
    }

    private async Task<bool> CanAccessJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        var assignment = await RequireAssignmentAsync(organizationId, cancellationToken);
        return assignment.IsSuccess && await repository.IsVisibleJobAsync(organizationId, jobId, cancellationToken);
    }

    private bool TryGetAuditorActor(out Guid userId, out Guid organizationId)
    {
        if (string.Equals(currentUser.Role, Roles.Auditor, StringComparison.OrdinalIgnoreCase)
            && currentUser.UserId is Guid actorUserId
            && currentUser.OrganizationId is Guid actorOrganizationId)
        {
            userId = actorUserId;
            organizationId = actorOrganizationId;
            return true;
        }
        userId = Guid.Empty;
        organizationId = Guid.Empty;
        return false;
    }

    private bool TryGetCompanyActor(out Guid userId, out Guid organizationId)
    {
        if (!string.Equals(currentUser.Role, Roles.Auditor, StringComparison.OrdinalIgnoreCase)
            && currentUser.UserId is Guid actorUserId
            && currentUser.OrganizationId is Guid actorOrganizationId)
        {
            userId = actorUserId;
            organizationId = actorOrganizationId;
            return true;
        }
        userId = Guid.Empty;
        organizationId = Guid.Empty;
        return false;
    }

    private bool IsSuperAdmin() => string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ValidationError>? ValidateFinding(string category, string description, string? reference)
    {
        var errors = new List<ValidationError>();
        if (!FindingCategories.Contains(category))
            errors.Add(new ValidationError { Identifier = nameof(category), ErrorMessage = "Kategori skal være A, An, Anb eller IR." });
        if (string.IsNullOrWhiteSpace(description))
            errors.Add(new ValidationError { Identifier = nameof(description), ErrorMessage = "Beskrivelse er påkrævet." });
        else if (description.Length > 4000)
            errors.Add(new ValidationError { Identifier = nameof(description), ErrorMessage = "Beskrivelsen må højst være 4000 tegn." });
        if (reference?.Length > 500)
            errors.Add(new ValidationError { Identifier = nameof(reference), ErrorMessage = "Referencen må højst være 500 tegn." });
        return errors.Count == 0 ? null : errors;
    }

    private static Result<T> Invalid<T>(string identifier, string message) =>
        Result<T>.Invalid([new ValidationError { Identifier = identifier, ErrorMessage = message }]);

    private static string NormalizeFindingCategory(string category) => category.Trim().ToLowerInvariant() switch
    {
        "a" => "A",
        "an" => "An",
        "anb" => "Anb",
        "ir" => "IR",
        _ => category.Trim()
    };

    private static string NormalizeFindingStatus(string status) =>
        FindingStatuses.First(candidate => string.Equals(candidate, status.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeInstallationFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return AllowedInstallationTypes.FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
