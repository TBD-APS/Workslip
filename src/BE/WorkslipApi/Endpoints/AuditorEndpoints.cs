using System.Data;
using System.Text.Json;
using Dapper;
using Workslip.Application.Auth;
using Workslip.Application.Images;
using Workslip.Domain;
using Workslip.Infrastructure;

namespace Workslip.Api.Endpoints;

public static class AuditorEndpoints
{
    private const string AuthorizationAreaVvs = "VVS";
    private const string FindingOpen = "Open";
    private const string FindingAwaitingEvidence = "AwaitingEvidence";
    private const string FindingReadyForVerification = "ReadyForVerification";
    private const string FindingClosed = "Closed";

    private static readonly HashSet<string> FindingCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "An", "Anb", "IR"
    };

    private static readonly HashSet<string> FindingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        FindingOpen,
        FindingAwaitingEvidence,
        FindingReadyForVerification,
        FindingClosed
    };

    public static IEndpointRouteBuilder MapAuditorEndpoints(this IEndpointRouteBuilder app)
    {
        var auditor = app.MapGroup("/api/auditor")
            .WithTags("auditor")
            .RequireAuthorization(AuthPolicies.RequireAuditor);

        auditor.MapGet("/organizations", ListOrganizationsAsync)
            .Produces<IReadOnlyList<AuditorOrganizationSummaryResponse>>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports", ListReportsAsync)
            .Produces<AuditorReportListResponse>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports/{jobId:guid}", GetReportAsync)
            .Produces<AuditorReportDetailResponse>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports/{jobId:guid}/images", ListImagesAsync)
            .Produces<IReadOnlyList<ImageInfoResponse>>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports/{jobId:guid}/images/{imageId:guid}", GetImageAsync);

        auditor.MapPost("/organizations/{organizationId:guid}/reports/{jobId:guid}/findings", CreateFindingAsync)
            .Produces<AuditorFindingResponse>(StatusCodes.Status201Created);

        auditor.MapPatch("/organizations/{organizationId:guid}/reports/{jobId:guid}/findings/{findingId:guid}", UpdateFindingAsync)
            .Produces<AuditorFindingResponse>();

        var management = app.MapGroup("/api/auditor/admin")
            .WithTags("auditor-admin")
            .RequireAuthorization(AuthPolicies.RequireSuperAdmin);

        management.MapGet("/assignments", ListAssignmentsAsync)
            .Produces<IReadOnlyList<AuditorAssignmentAdminResponse>>();

        management.MapPost("/assignments", CreateAssignmentAsync)
            .Produces<AuditorAssignmentAdminResponse>(StatusCodes.Status201Created);

        management.MapPatch("/assignments/{assignmentId:guid}", UpdateAssignmentAsync)
            .Produces<AuditorAssignmentAdminResponse>();

        var company = app.MapGroup("/api/auditor/findings")
            .WithTags("auditor-follow-up")
            .RequireAuthorization(AuthPolicies.RequireUser);

        company.MapPost("/{findingId:guid}/evidence", AddCompanyEvidenceAsync)
            .Produces<AuditorFindingResponse>();

        return app;
    }

    private static async Task<IResult> ListOrganizationsAsync(
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        const string sql = """
            SELECT
                a.Id AS AssignmentId,
                o.Id AS OrganizationId,
                o.Name AS OrganizationName,
                o.Cvr,
                a.AuthorizationArea,
                a.ActiveFrom,
                a.ActiveUntil,
                (
                    SELECT COUNT(1)
                    FROM dbo.AuditorFindings f
                    WHERE f.AssignmentId = a.Id
                      AND f.Status <> N'Closed'
                ) AS OpenFindings,
                (
                    SELECT MAX(e.CreatedAt)
                    FROM dbo.AuditorEvents e
                    WHERE e.AssignmentId = a.Id
                ) AS LastActivityAt
            FROM dbo.AuditorAssignments a
            INNER JOIN dbo.Organizations o ON o.Id = a.TargetOrganizationId
            WHERE a.AuditorUserId = @UserId
              AND a.ControlOrganizationId = @ControlOrganizationId
              AND a.AuthorizationArea = @AuthorizationArea
              AND a.IsActive = 1
              AND a.ActiveFrom <= sysutcdatetime()
              AND (a.ActiveUntil IS NULL OR a.ActiveUntil > sysutcdatetime())
            ORDER BY o.Name ASC, o.Id ASC;
            """;

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditorOrganizationSummaryResponse>(new CommandDefinition(
            sql,
            new { UserId = userId, ControlOrganizationId = controlOrganizationId, AuthorizationArea = AuthorizationAreaVvs },
            cancellationToken: cancellationToken));
        return Results.Ok(rows.AsList());
    }

    private static async Task<IResult> ListReportsAsync(
        Guid organizationId,
        string? search,
        string? installationType,
        int? limit,
        int? offset,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var assignment = await GetActiveAssignmentAsync(
            connection,
            userId,
            controlOrganizationId,
            organizationId,
            cancellationToken);
        if (assignment is null)
            return Results.NotFound();

        var requestedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var requestedOffset = Math.Max(offset ?? 0, 0);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedInstallationType = NormalizeInstallationFilter(installationType);
        if (installationType is not null && normalizedInstallationType is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(installationType)] = ["Anlægstype skal være Vand eller Afløb."]
            });
        }

        const string sql = """
            SELECT
                jr.Id,
                jr.ReportNumber,
                jr.CustomerName,
                COALESCE(NULLIF(jr.DestinationAddress, N''), jr.CustomerAddress) AS Address,
                jr.Status,
                jr.ReportDate,
                jr.UpdatedAt,
                jr.SubmittedAt,
                submittedBy.DisplayName AS SubmittedBy,
                installationNames.InstallationTypes,
                assignedUsers.AssignedUsers,
                hours.TotalHours,
                (
                    SELECT COUNT(1)
                    FROM dbo.AuditorFindings f
                    WHERE f.TargetOrganizationId = jr.OrganizationId
                      AND f.JobReportId = jr.Id
                      AND f.Status <> N'Closed'
                ) AS OpenFindings,
                COUNT(1) OVER() AS TotalCount
            FROM dbo.JobReports jr
            LEFT JOIN dbo.Users submittedBy
              ON submittedBy.Id = jr.SubmittedByUserId
             AND submittedBy.OrganizationId = jr.OrganizationId
            OUTER APPLY
            (
                SELECT STRING_AGG(types.Name, N', ') WITHIN GROUP (ORDER BY types.Name) AS InstallationTypes
                FROM
                (
                    SELECT DISTINCT itd.Name
                    FROM dbo.JobReportInstallations i
                    INNER JOIN dbo.InstallationTypeDefinitions itd
                      ON itd.Id = i.InstallationTypeDefinitionId
                     AND itd.OrganizationId = i.OrganizationId
                    WHERE i.OrganizationId = jr.OrganizationId
                      AND i.JobReportId = jr.Id
                      AND itd.Name IN (N'Vand', N'Afløb')
                ) types
            ) installationNames
            OUTER APPLY
            (
                SELECT STRING_AGG(users.DisplayName, N', ') WITHIN GROUP (ORDER BY users.DisplayName) AS AssignedUsers
                FROM
                (
                    SELECT DISTINCT u.DisplayName
                    FROM dbo.JobAssignments ja
                    INNER JOIN dbo.Users u
                      ON u.Id = ja.UserId
                     AND u.OrganizationId = ja.OrganizationId
                    WHERE ja.OrganizationId = jr.OrganizationId
                      AND ja.ReportId = jr.Id
                ) users
            ) assignedUsers
            OUTER APPLY
            (
                SELECT SUM(w.HoursWorked) AS TotalHours
                FROM dbo.Worksheets w
                WHERE w.OrganizationId = jr.OrganizationId
                  AND w.JobId = jr.Id
            ) hours
            WHERE jr.OrganizationId = @OrganizationId
              AND jr.Status = N'Approved'
              AND jr.IsSoftDeleted = 0
              AND jr.IsInAuditorScope = 1
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.JobReportInstallations visibleInstall
                  INNER JOIN dbo.InstallationTypeDefinitions visibleType
                    ON visibleType.Id = visibleInstall.InstallationTypeDefinitionId
                   AND visibleType.OrganizationId = visibleInstall.OrganizationId
                  WHERE visibleInstall.OrganizationId = jr.OrganizationId
                    AND visibleInstall.JobReportId = jr.Id
                    AND visibleType.Name IN (N'Vand', N'Afløb')
                    AND (@InstallationType IS NULL OR visibleType.Name = @InstallationType)
              )
              AND
              (
                  @Search IS NULL
                  OR jr.ReportNumber LIKE N'%' + @Search + N'%'
                  OR jr.CustomerName LIKE N'%' + @Search + N'%'
                  OR jr.CustomerAddress LIKE N'%' + @Search + N'%'
                  OR jr.DestinationAddress LIKE N'%' + @Search + N'%'
              )
            ORDER BY jr.UpdatedAt DESC, jr.Id DESC
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
            """;

        var rows = (await connection.QueryAsync<AuditorReportDbRow>(new CommandDefinition(
            sql,
            new
            {
                OrganizationId = organizationId,
                Search = normalizedSearch,
                InstallationType = normalizedInstallationType,
                Limit = requestedLimit,
                Offset = requestedOffset
            },
            cancellationToken: cancellationToken))).AsList();

        var items = rows.Select(row => new AuditorReportListItemResponse(
            row.Id,
            row.ReportNumber,
            row.CustomerName,
            row.Address,
            row.Status,
            row.InstallationTypes,
            row.TotalHours,
            row.AssignedUsers,
            row.SubmittedBy,
            row.ReportDate,
            row.SubmittedAt,
            row.UpdatedAt,
            row.OpenFindings)).ToArray();

        return Results.Ok(new AuditorReportListResponse(items, rows.FirstOrDefault()?.TotalCount ?? 0));
    }

    private static async Task<IResult> GetReportAsync(
        Guid organizationId,
        Guid jobId,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        IImageStorage imageStorage,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var assignment = await GetActiveAssignmentAsync(
            connection,
            userId,
            controlOrganizationId,
            organizationId,
            cancellationToken);
        if (assignment is null || !await IsAuditorVisibleJobAsync(connection, organizationId, jobId, cancellationToken))
            return Results.NotFound();

        const string reportSql = """
            SELECT TOP (1)
                jr.Id,
                jr.ReportNumber,
                jr.CustomerName,
                jr.CustomerEmail,
                jr.CustomerPhone,
                jr.CustomerAddress,
                jr.CustomerContactPerson,
                jr.DestinationAddress,
                jr.DestinationZipCode,
                jr.DestinationCity,
                jr.Status,
                jr.JobType,
                jr.ReportDate,
                jr.TaskDescription,
                jr.CustomerObservations,
                jr.TechnicalObservations,
                jr.Remarks,
                jr.CreatedAt,
                jr.UpdatedAt,
                jr.SubmittedAt,
                submittedBy.DisplayName AS SubmittedBy,
                o.Name AS OrganizationName,
                o.Cvr AS OrganizationCvr
            FROM dbo.JobReports jr
            INNER JOIN dbo.Organizations o ON o.Id = jr.OrganizationId
            LEFT JOIN dbo.Users submittedBy
              ON submittedBy.Id = jr.SubmittedByUserId
             AND submittedBy.OrganizationId = jr.OrganizationId
            WHERE jr.OrganizationId = @OrganizationId
              AND jr.Id = @JobId;
            """;

        var report = await connection.QuerySingleOrDefaultAsync<AuditorReportDetailDbRow>(new CommandDefinition(
            reportSql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken));
        if (report is null)
            return Results.NotFound();

        var installationRows = (await connection.QueryAsync<AuditorControlPointDbRow>(new CommandDefinition(
            InstallationSql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken))).AsList();

        var installations = installationRows
            .GroupBy(row => new { row.InstallationId, row.InstallationTypeName, row.InstallationSortOrder })
            .OrderBy(group => group.Key.InstallationSortOrder)
            .Select(group => new AuditorInstallationResponse(
                group.Key.InstallationId,
                group.Key.InstallationTypeName,
                group
                    .Where(row => row.CategoryId is not null)
                    .GroupBy(row => new { row.CategoryId, row.CategoryName, row.CategorySortOrder, row.IsIrrelevant })
                    .OrderBy(category => category.Key.CategorySortOrder)
                    .Select(category => new AuditorControlCategoryResponse(
                        category.Key.CategoryId!.Value,
                        category.Key.CategoryName ?? string.Empty,
                        category.Key.IsIrrelevant ?? false,
                        category
                            .Where(row => row.ControlPointId is not null)
                            .OrderBy(row => row.ControlPointSortOrder)
                            .Select(row => new AuditorControlPointResponse(
                                row.ControlPointId!.Value,
                                row.ControlPointName ?? string.Empty,
                                row.IsRequired ?? false,
                                row.IsChecked ?? false))
                            .ToArray()))
                    .ToArray()))
            .ToArray();

        const string assignedSql = """
            SELECT u.Id, u.DisplayName, u.Email
            FROM dbo.JobAssignments ja
            INNER JOIN dbo.Users u
              ON u.Id = ja.UserId
             AND u.OrganizationId = ja.OrganizationId
            WHERE ja.OrganizationId = @OrganizationId
              AND ja.ReportId = @JobId
            ORDER BY u.DisplayName ASC, u.Id ASC;
            """;
        var assigned = (await connection.QueryAsync<AuditorPersonResponse>(new CommandDefinition(
            assignedSql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken))).AsList();

        const string worksheetSql = """
            SELECT w.Id, w.WorkDate, w.HoursWorked, u.Id AS UserId, u.DisplayName AS UserName
            FROM dbo.Worksheets w
            INNER JOIN dbo.Users u
              ON u.Id = w.UserId
             AND u.OrganizationId = w.OrganizationId
            WHERE w.OrganizationId = @OrganizationId
              AND w.JobId = @JobId
            ORDER BY w.WorkDate ASC, u.DisplayName ASC, w.Id ASC;
            """;
        var worksheets = (await connection.QueryAsync<AuditorWorksheetResponse>(new CommandDefinition(
            worksheetSql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken))).AsList();

        var findings = await ListFindingsAsync(connection, organizationId, jobId, cancellationToken);
        var events = await ListAuditorEventsAsync(connection, organizationId, jobId, cancellationToken);
        var images = await imageStorage.ListJobImagesAsync(organizationId, jobId, cancellationToken);

        await WriteEventAsync(
            connection,
            transaction: null,
            assignment.Id,
            organizationId,
            jobId,
            findingId: null,
            userId,
            "ReportOpened",
            details: null,
            cancellationToken);

        return Results.Ok(new AuditorReportDetailResponse(
            report.Id,
            report.OrganizationName,
            report.OrganizationCvr,
            report.ReportNumber,
            report.CustomerName,
            report.CustomerEmail,
            report.CustomerPhone,
            report.CustomerAddress,
            report.CustomerContactPerson,
            report.DestinationAddress,
            report.DestinationZipCode,
            report.DestinationCity,
            report.Status,
            report.JobType,
            report.ReportDate,
            report.TaskDescription,
            report.CustomerObservations,
            report.TechnicalObservations,
            report.Remarks,
            report.CreatedAt,
            report.UpdatedAt,
            report.SubmittedAt,
            report.SubmittedBy,
            installations,
            assigned,
            worksheets,
            findings,
            events,
            images));
    }

    private static async Task<IResult> ListImagesAsync(
        Guid organizationId,
        Guid jobId,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        IImageStorage imageStorage,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        if (await GetActiveAssignmentAsync(connection, userId, controlOrganizationId, organizationId, cancellationToken) is null
            || !await IsAuditorVisibleJobAsync(connection, organizationId, jobId, cancellationToken))
        {
            return Results.NotFound();
        }

        var images = await imageStorage.ListJobImagesAsync(organizationId, jobId, cancellationToken);
        return Results.Ok(images);
    }

    private static async Task<IResult> GetImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        IImageStorage imageStorage,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        if (await GetActiveAssignmentAsync(connection, userId, controlOrganizationId, organizationId, cancellationToken) is null
            || !await IsAuditorVisibleJobAsync(connection, organizationId, jobId, cancellationToken))
        {
            return Results.NotFound();
        }

        var image = await imageStorage.GetJobImageAsync(organizationId, jobId, imageId, cancellationToken);
        return image is null
            ? Results.NotFound()
            : Results.Stream(image.Content, image.ContentType, enableRangeProcessing: false);
    }

    private static async Task<IResult> CreateFindingAsync(
        Guid organizationId,
        Guid jobId,
        CreateAuditorFindingRequest request,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        var validation = ValidateFinding(request.Category, request.Description, request.Reference);
        if (validation is not null)
            return validation;

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var assignment = await GetActiveAssignmentAsync(connection, userId, controlOrganizationId, organizationId, cancellationToken);
        if (assignment is null || !await IsAuditorVisibleJobAsync(connection, organizationId, jobId, cancellationToken))
            return Results.NotFound();

        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var findingId = Guid.NewGuid();
        const string insertSql = """
            INSERT INTO dbo.AuditorFindings
            (
                Id, AssignmentId, TargetOrganizationId, JobReportId, Category, Description, Reference,
                DueAt, Status, CreatedByAuditorUserId, CreatedAt, UpdatedAt
            )
            VALUES
            (
                @Id, @AssignmentId, @TargetOrganizationId, @JobReportId, @Category, @Description, @Reference,
                @DueAt, @Status, @CreatedByAuditorUserId, @CreatedAt, @UpdatedAt
            );
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            insertSql,
            new
            {
                Id = findingId,
                AssignmentId = assignment.Id,
                TargetOrganizationId = organizationId,
                JobReportId = jobId,
                Category = NormalizeFindingCategory(request.Category),
                Description = request.Description.Trim(),
                Reference = NormalizeOptionalText(request.Reference),
                request.DueAt,
                Status = FindingOpen,
                CreatedByAuditorUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        await WriteEventAsync(
            connection,
            transaction,
            assignment.Id,
            organizationId,
            jobId,
            findingId,
            userId,
            "FindingCreated",
            JsonSerializer.Serialize(new { category = NormalizeFindingCategory(request.Category), request.DueAt }),
            cancellationToken);
        transaction.Commit();

        var finding = await GetFindingAsync(connection, findingId, cancellationToken);
        return Results.Created($"/api/auditor/organizations/{organizationId}/reports/{jobId}", finding);
    }

    private static async Task<IResult> UpdateFindingAsync(
        Guid organizationId,
        Guid jobId,
        Guid findingId,
        UpdateAuditorFindingRequest request,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(currentUser, out var userId, out var controlOrganizationId))
            return Results.Unauthorized();

        if (!FindingStatuses.Contains(request.Status))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Status)] = ["Ukendt fundstatus."]
            });
        }

        if (request.Description is not null && string.IsNullOrWhiteSpace(request.Description))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Description)] = ["Beskrivelsen må ikke være tom."]
            });
        }
        if (request.Description?.Length > 4000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Description)] = ["Beskrivelsen må højst være 4000 tegn."] });

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var assignment = await GetActiveAssignmentAsync(connection, userId, controlOrganizationId, organizationId, cancellationToken);
        if (assignment is null || !await IsAuditorVisibleJobAsync(connection, organizationId, jobId, cancellationToken))
            return Results.NotFound();

        const string ownershipSql = """
            SELECT COUNT(1)
            FROM dbo.AuditorFindings
            WHERE Id = @FindingId
              AND TargetOrganizationId = @OrganizationId
              AND JobReportId = @JobId;
            """;
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownershipSql,
            new { FindingId = findingId, OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken));
        if (exists == 0)
            return Results.NotFound();

        var normalizedStatus = NormalizeFindingStatus(request.Status);
        var now = DateTimeOffset.UtcNow;
        using var transaction = connection.BeginTransaction();
        const string updateSql = """
            UPDATE dbo.AuditorFindings
            SET Description = COALESCE(@Description, Description),
                DueAt = @DueAt,
                Status = @Status,
                VerifiedByAuditorUserId = CASE WHEN @Status = N'Closed' THEN @ActorUserId ELSE NULL END,
                VerifiedAt = CASE WHEN @Status = N'Closed' THEN @UpdatedAt ELSE NULL END,
                UpdatedAt = @UpdatedAt
            WHERE Id = @FindingId
              AND TargetOrganizationId = @OrganizationId
              AND JobReportId = @JobId;
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new
            {
                FindingId = findingId,
                OrganizationId = organizationId,
                JobId = jobId,
                Description = request.Description is null ? null : request.Description.Trim(),
                request.DueAt,
                Status = normalizedStatus,
                ActorUserId = userId,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        await WriteEventAsync(
            connection,
            transaction,
            assignment.Id,
            organizationId,
            jobId,
            findingId,
            userId,
            normalizedStatus == FindingClosed ? "FindingClosed" : "FindingUpdated",
            JsonSerializer.Serialize(new { status = normalizedStatus, request.DueAt }),
            cancellationToken);
        transaction.Commit();

        return Results.Ok(await GetFindingAsync(connection, findingId, cancellationToken));
    }

    private static async Task<IResult> AddCompanyEvidenceAsync(
        Guid findingId,
        AddAuditorFindingEvidenceRequest request,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId || currentUser.OrganizationId is not Guid organizationId)
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Evidence))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Evidence)] = ["Dokumentation/svar er påkrævet."] });
        if (request.Evidence.Length > 8000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Evidence)] = ["Dokumentation/svar må højst være 8000 tegn."] });

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        const string findingSql = """
            SELECT TOP (1) AssignmentId, TargetOrganizationId, JobReportId, Status
            FROM dbo.AuditorFindings
            WHERE Id = @FindingId
              AND TargetOrganizationId = @OrganizationId;
            """;
        var target = await connection.QuerySingleOrDefaultAsync<AuditorFindingTargetRow>(new CommandDefinition(
            findingSql,
            new { FindingId = findingId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));
        if (target is null)
            return Results.NotFound();
        if (string.Equals(target.Status, FindingClosed, StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { error = "finding_closed", message = "Et lukket fund kan ikke få ny dokumentation uden først at blive genåbnet af auditor." });

        var now = DateTimeOffset.UtcNow;
        using var transaction = connection.BeginTransaction();
        const string updateSql = """
            UPDATE dbo.AuditorFindings
            SET CompanyEvidence = @Evidence,
                CompanyEvidenceByUserId = @ActorUserId,
                CompanyEvidenceAt = @UpdatedAt,
                Status = N'ReadyForVerification',
                UpdatedAt = @UpdatedAt
            WHERE Id = @FindingId
              AND TargetOrganizationId = @OrganizationId;
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new
            {
                FindingId = findingId,
                OrganizationId = organizationId,
                Evidence = request.Evidence.Trim(),
                ActorUserId = userId,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        await WriteEventAsync(
            connection,
            transaction,
            target.AssignmentId,
            organizationId,
            target.JobReportId,
            findingId,
            userId,
            "CompanyEvidenceAdded",
            details: null,
            cancellationToken);
        transaction.Commit();

        return Results.Ok(await GetFindingAsync(connection, findingId, cancellationToken));
    }

    private static async Task<IResult> ListAssignmentsAsync(
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                a.Id,
                a.AuditorUserId,
                u.DisplayName AS AuditorName,
                u.Email AS AuditorEmail,
                a.ControlOrganizationId,
                control.Name AS ControlOrganizationName,
                a.TargetOrganizationId,
                target.Name AS TargetOrganizationName,
                target.Cvr AS TargetOrganizationCvr,
                a.AuthorizationArea,
                a.ActiveFrom,
                a.ActiveUntil,
                a.IsActive,
                a.CreatedAt,
                a.UpdatedAt
            FROM dbo.AuditorAssignments a
            INNER JOIN dbo.Users u ON u.Id = a.AuditorUserId
            INNER JOIN dbo.Organizations control ON control.Id = a.ControlOrganizationId
            INNER JOIN dbo.Organizations target ON target.Id = a.TargetOrganizationId
            ORDER BY a.IsActive DESC, target.Name ASC, u.DisplayName ASC, a.CreatedAt DESC;
            """;
        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditorAssignmentAdminResponse>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return Results.Ok(rows.AsList());
    }

    private static async Task<IResult> CreateAssignmentAsync(
        CreateAuditorAssignmentRequest request,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actorUserId)
            return Results.Unauthorized();

        var activeFrom = request.ActiveFrom ?? DateTimeOffset.UtcNow;
        if (request.ActiveUntil is not null && request.ActiveUntil <= activeFrom)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ActiveUntil)] = ["Slutdato skal ligge efter startdato."]
            });
        }
        if (!string.Equals(request.AuthorizationArea?.Trim(), AuthorizationAreaVvs, StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.AuthorizationArea)] = ["Auditor v1 understøtter fagområdet VVS."]
            });
        }

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        const string auditorSql = """
            SELECT TOP (1) Id, OrganizationId, DisplayName, Email, Role
            FROM dbo.Users
            WHERE Id = @AuditorUserId;
            """;
        var auditor = await connection.QuerySingleOrDefaultAsync<AssignmentAuditorRow>(new CommandDefinition(
            auditorSql,
            new { request.AuditorUserId },
            cancellationToken: cancellationToken));
        if (auditor is null || !string.Equals(auditor.Role, Roles.Auditor, StringComparison.OrdinalIgnoreCase))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AuditorUserId)] = ["Brugeren findes ikke eller har ikke auditor-rollen."] });

        const string targetSql = "SELECT COUNT(1) FROM dbo.Organizations WHERE Id = @TargetOrganizationId;";
        var targetExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            targetSql,
            new { request.TargetOrganizationId },
            cancellationToken: cancellationToken));
        if (targetExists == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.TargetOrganizationId)] = ["Målorganisationen findes ikke."] });

        const string duplicateSql = """
            SELECT COUNT(1)
            FROM dbo.AuditorAssignments
            WHERE AuditorUserId = @AuditorUserId
              AND TargetOrganizationId = @TargetOrganizationId
              AND AuthorizationArea = @AuthorizationArea
              AND IsActive = 1
              AND (ActiveUntil IS NULL OR ActiveUntil > sysutcdatetime());
            """;
        var duplicate = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            duplicateSql,
            new { request.AuditorUserId, request.TargetOrganizationId, AuthorizationArea = AuthorizationAreaVvs },
            cancellationToken: cancellationToken));
        if (duplicate > 0)
            return Results.Conflict(new { error = "active_assignment_exists", message = "Auditoren har allerede en aktiv VVS-tilknytning til organisationen." });

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using var transaction = connection.BeginTransaction();
        const string insertSql = """
            INSERT INTO dbo.AuditorAssignments
            (
                Id, AuditorUserId, ControlOrganizationId, TargetOrganizationId, AuthorizationArea,
                ActiveFrom, ActiveUntil, IsActive, CreatedByUserId, UpdatedByUserId, CreatedAt, UpdatedAt
            )
            VALUES
            (
                @Id, @AuditorUserId, @ControlOrganizationId, @TargetOrganizationId, @AuthorizationArea,
                @ActiveFrom, @ActiveUntil, 1, @ActorUserId, @ActorUserId, @CreatedAt, @UpdatedAt
            );
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            insertSql,
            new
            {
                Id = id,
                request.AuditorUserId,
                ControlOrganizationId = auditor.OrganizationId,
                request.TargetOrganizationId,
                AuthorizationArea = AuthorizationAreaVvs,
                ActiveFrom = activeFrom,
                request.ActiveUntil,
                ActorUserId = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));
        await WriteEventAsync(
            connection,
            transaction,
            id,
            request.TargetOrganizationId,
            jobId: null,
            findingId: null,
            actorUserId,
            "AssignmentCreated",
            JsonSerializer.Serialize(new { request.AuditorUserId, controlOrganizationId = auditor.OrganizationId, authorizationArea = AuthorizationAreaVvs, activeFrom, request.ActiveUntil }),
            cancellationToken);
        transaction.Commit();

        return Results.Created($"/api/auditor/admin/assignments/{id}", await GetAssignmentAdminAsync(connection, id, cancellationToken));
    }

    private static async Task<IResult> UpdateAssignmentAsync(
        Guid assignmentId,
        UpdateAuditorAssignmentRequest request,
        ICurrentUserContext currentUser,
        ISqlConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actorUserId)
            return Results.Unauthorized();

        using var connection = await connections.OpenConnectionAsync(cancellationToken);
        const string targetSql = """
            SELECT TOP (1) TargetOrganizationId, ActiveFrom
            FROM dbo.AuditorAssignments
            WHERE Id = @AssignmentId;
            """;
        var target = await connection.QuerySingleOrDefaultAsync<AssignmentTargetRow>(new CommandDefinition(
            targetSql,
            new { AssignmentId = assignmentId },
            cancellationToken: cancellationToken));
        if (target is null)
            return Results.NotFound();
        if (request.ActiveUntil is not null && request.ActiveUntil <= target.ActiveFrom)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ActiveUntil)] = ["Slutdato skal ligge efter startdato."] });

        var now = DateTimeOffset.UtcNow;
        using var transaction = connection.BeginTransaction();
        const string updateSql = """
            UPDATE dbo.AuditorAssignments
            SET IsActive = @IsActive,
                ActiveUntil = @ActiveUntil,
                UpdatedByUserId = @ActorUserId,
                UpdatedAt = @UpdatedAt
            WHERE Id = @AssignmentId;
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { AssignmentId = assignmentId, request.IsActive, request.ActiveUntil, ActorUserId = actorUserId, UpdatedAt = now },
            transaction,
            cancellationToken: cancellationToken));
        await WriteEventAsync(
            connection,
            transaction,
            assignmentId,
            target.TargetOrganizationId,
            jobId: null,
            findingId: null,
            actorUserId,
            request.IsActive ? "AssignmentUpdated" : "AssignmentDeactivated",
            JsonSerializer.Serialize(new { request.IsActive, request.ActiveUntil }),
            cancellationToken);
        transaction.Commit();
        return Results.Ok(await GetAssignmentAdminAsync(connection, assignmentId, cancellationToken));
    }

    private static async Task<AuditorAssignmentRow?> GetActiveAssignmentAsync(
        IDbConnection connection,
        Guid auditorUserId,
        Guid controlOrganizationId,
        Guid targetOrganizationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) Id, AuditorUserId, ControlOrganizationId, TargetOrganizationId, AuthorizationArea, ActiveFrom, ActiveUntil
            FROM dbo.AuditorAssignments
            WHERE AuditorUserId = @AuditorUserId
              AND ControlOrganizationId = @ControlOrganizationId
              AND TargetOrganizationId = @TargetOrganizationId
              AND AuthorizationArea = @AuthorizationArea
              AND IsActive = 1
              AND ActiveFrom <= sysutcdatetime()
              AND (ActiveUntil IS NULL OR ActiveUntil > sysutcdatetime())
            ORDER BY ActiveFrom DESC, CreatedAt DESC;
            """;
        return await connection.QuerySingleOrDefaultAsync<AuditorAssignmentRow>(new CommandDefinition(
            sql,
            new
            {
                AuditorUserId = auditorUserId,
                ControlOrganizationId = controlOrganizationId,
                TargetOrganizationId = targetOrganizationId,
                AuthorizationArea = AuthorizationAreaVvs
            },
            cancellationToken: cancellationToken));
    }

    private static async Task<bool> IsAuditorVisibleJobAsync(
        IDbConnection connection,
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.JobReports jr
            WHERE jr.OrganizationId = @OrganizationId
              AND jr.Id = @JobId
              AND jr.Status = N'Approved'
              AND jr.IsSoftDeleted = 0
              AND jr.IsInAuditorScope = 1
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.JobReportInstallations i
                  INNER JOIN dbo.InstallationTypeDefinitions itd
                    ON itd.Id = i.InstallationTypeDefinitionId
                   AND itd.OrganizationId = i.OrganizationId
                  WHERE i.OrganizationId = jr.OrganizationId
                    AND i.JobReportId = jr.Id
                    AND itd.Name IN (N'Vand', N'Afløb')
              );
            """;
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken)) > 0;
    }

    private static async Task<IReadOnlyList<AuditorFindingResponse>> ListFindingsAsync(
        IDbConnection connection,
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                f.Id, f.AssignmentId, f.Category, f.Description, f.Reference, f.DueAt, f.Status,
                f.CompanyEvidence, f.CompanyEvidenceAt,
                createdBy.DisplayName AS CreatedBy,
                evidenceBy.DisplayName AS CompanyEvidenceBy,
                verifiedBy.DisplayName AS VerifiedBy,
                f.VerifiedAt, f.CreatedAt, f.UpdatedAt
            FROM dbo.AuditorFindings f
            LEFT JOIN dbo.Users createdBy ON createdBy.Id = f.CreatedByAuditorUserId
            LEFT JOIN dbo.Users evidenceBy ON evidenceBy.Id = f.CompanyEvidenceByUserId
            LEFT JOIN dbo.Users verifiedBy ON verifiedBy.Id = f.VerifiedByAuditorUserId
            WHERE f.TargetOrganizationId = @OrganizationId
              AND f.JobReportId = @JobId
            ORDER BY CASE WHEN f.Status = N'Closed' THEN 1 ELSE 0 END ASC, f.CreatedAt DESC;
            """;
        var rows = await connection.QueryAsync<AuditorFindingResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<AuditorFindingResponse?> GetFindingAsync(
        IDbConnection connection,
        Guid findingId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                f.Id, f.AssignmentId, f.Category, f.Description, f.Reference, f.DueAt, f.Status,
                f.CompanyEvidence, f.CompanyEvidenceAt,
                createdBy.DisplayName AS CreatedBy,
                evidenceBy.DisplayName AS CompanyEvidenceBy,
                verifiedBy.DisplayName AS VerifiedBy,
                f.VerifiedAt, f.CreatedAt, f.UpdatedAt
            FROM dbo.AuditorFindings f
            LEFT JOIN dbo.Users createdBy ON createdBy.Id = f.CreatedByAuditorUserId
            LEFT JOIN dbo.Users evidenceBy ON evidenceBy.Id = f.CompanyEvidenceByUserId
            LEFT JOIN dbo.Users verifiedBy ON verifiedBy.Id = f.VerifiedByAuditorUserId
            WHERE f.Id = @FindingId;
            """;
        return await connection.QuerySingleOrDefaultAsync<AuditorFindingResponse>(new CommandDefinition(
            sql,
            new { FindingId = findingId },
            cancellationToken: cancellationToken));
    }

    private static async Task<IReadOnlyList<AuditorEventResponse>> ListAuditorEventsAsync(
        IDbConnection connection,
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT e.Id, e.EventType, e.DetailsJson, e.CreatedAt, u.DisplayName AS ActorName
            FROM dbo.AuditorEvents e
            LEFT JOIN dbo.Users u ON u.Id = e.ActorUserId
            WHERE e.TargetOrganizationId = @OrganizationId
              AND e.JobReportId = @JobId
            ORDER BY e.CreatedAt DESC, e.Id DESC;
            """;
        var rows = await connection.QueryAsync<AuditorEventResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task WriteEventAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid? assignmentId,
        Guid targetOrganizationId,
        Guid? jobId,
        Guid? findingId,
        Guid actorUserId,
        string eventType,
        string? details,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.AuditorEvents
                (Id, AssignmentId, TargetOrganizationId, JobReportId, FindingId, ActorUserId, EventType, DetailsJson, CreatedAt)
            VALUES
                (@Id, @AssignmentId, @TargetOrganizationId, @JobReportId, @FindingId, @ActorUserId, @EventType, @DetailsJson, @CreatedAt);
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = Guid.NewGuid(),
                AssignmentId = assignmentId,
                TargetOrganizationId = targetOrganizationId,
                JobReportId = jobId,
                FindingId = findingId,
                ActorUserId = actorUserId,
                EventType = eventType,
                DetailsJson = details,
                CreatedAt = DateTimeOffset.UtcNow
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<AuditorAssignmentAdminResponse?> GetAssignmentAdminAsync(
        IDbConnection connection,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                a.Id,
                a.AuditorUserId,
                u.DisplayName AS AuditorName,
                u.Email AS AuditorEmail,
                a.ControlOrganizationId,
                control.Name AS ControlOrganizationName,
                a.TargetOrganizationId,
                target.Name AS TargetOrganizationName,
                target.Cvr AS TargetOrganizationCvr,
                a.AuthorizationArea,
                a.ActiveFrom,
                a.ActiveUntil,
                a.IsActive,
                a.CreatedAt,
                a.UpdatedAt
            FROM dbo.AuditorAssignments a
            INNER JOIN dbo.Users u ON u.Id = a.AuditorUserId
            INNER JOIN dbo.Organizations control ON control.Id = a.ControlOrganizationId
            INNER JOIN dbo.Organizations target ON target.Id = a.TargetOrganizationId
            WHERE a.Id = @AssignmentId;
            """;
        return await connection.QuerySingleOrDefaultAsync<AuditorAssignmentAdminResponse>(new CommandDefinition(
            sql,
            new { AssignmentId = assignmentId },
            cancellationToken: cancellationToken));
    }

    private static bool TryGetActor(ICurrentUserContext currentUser, out Guid userId, out Guid organizationId)
    {
        if (currentUser.UserId is Guid actorUserId && currentUser.OrganizationId is Guid actorOrganizationId)
        {
            userId = actorUserId;
            organizationId = actorOrganizationId;
            return true;
        }

        userId = Guid.Empty;
        organizationId = Guid.Empty;
        return false;
    }

    private static IResult? ValidateFinding(string category, string description, string? reference)
    {
        var errors = new Dictionary<string, string[]>();
        if (!FindingCategories.Contains(category))
            errors[nameof(category)] = ["Kategori skal være A, An, Anb eller IR."];
        if (string.IsNullOrWhiteSpace(description))
            errors[nameof(description)] = ["Beskrivelse er påkrævet."];
        else if (description.Length > 4000)
            errors[nameof(description)] = ["Beskrivelsen må højst være 4000 tegn."];
        if (reference?.Length > 500)
            errors[nameof(reference)] = ["Referencen må højst være 500 tegn."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static string NormalizeFindingCategory(string category) =>
        category.Trim().ToLowerInvariant() switch
        {
            "a" => "A",
            "an" => "An",
            "anb" => "Anb",
            "ir" => "IR",
            _ => category.Trim()
        };

    private static string NormalizeFindingStatus(string status) =>
        FindingStatuses.First(candidate => string.Equals(candidate, status.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeInstallationFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (string.Equals(value.Trim(), "Vand", StringComparison.OrdinalIgnoreCase)) return "Vand";
        if (string.Equals(value.Trim(), "Afløb", StringComparison.OrdinalIgnoreCase)) return "Afløb";
        return null;
    }

    private const string InstallationSql = """
        SELECT
            i.Id AS InstallationId,
            i.SortOrder AS InstallationSortOrder,
            itd.Name AS InstallationTypeName,
            category.Id AS CategoryId,
            category.SortOrder AS CategorySortOrder,
            category.IsIrrelevant,
            cc.Name AS CategoryName,
            point.ControlPointId,
            point.SortOrder AS ControlPointSortOrder,
            point.IsRequired,
            point.IsChecked,
            cp.Name AS ControlPointName
        FROM dbo.JobReportInstallations i
        INNER JOIN dbo.InstallationTypeDefinitions itd
          ON itd.Id = i.InstallationTypeDefinitionId
         AND itd.OrganizationId = i.OrganizationId
        LEFT JOIN dbo.JobReportInstallationCategories category
          ON category.JobReportInstallationId = i.Id
         AND category.OrganizationId = i.OrganizationId
        LEFT JOIN dbo.ControlCategories cc
          ON cc.Id = category.ControlCategoryId
         AND cc.OrganizationId = category.OrganizationId
        LEFT JOIN dbo.JobReportInstallationControlPoints point
          ON point.JobReportInstallationCategoryId = category.Id
         AND point.OrganizationId = category.OrganizationId
        LEFT JOIN dbo.ControlPoints cp
          ON cp.Id = point.ControlPointId
         AND cp.OrganizationId = point.OrganizationId
        WHERE i.OrganizationId = @OrganizationId
          AND i.JobReportId = @JobId
          AND itd.Name IN (N'Vand', N'Afløb')
        ORDER BY i.SortOrder ASC, category.SortOrder ASC, point.SortOrder ASC;
        """;

    public sealed record AuditorOrganizationSummaryResponse(
        Guid AssignmentId,
        Guid OrganizationId,
        string OrganizationName,
        string Cvr,
        string AuthorizationArea,
        DateTimeOffset ActiveFrom,
        DateTimeOffset? ActiveUntil,
        int OpenFindings,
        DateTimeOffset? LastActivityAt);

    public sealed record AuditorReportListResponse(
        IReadOnlyList<AuditorReportListItemResponse> Items,
        int TotalCount);

    public sealed record AuditorReportListItemResponse(
        Guid Id,
        string? ReportNumber,
        string? CustomerName,
        string? Address,
        string Status,
        string? InstallationTypes,
        decimal? TotalHours,
        string? AssignedUsers,
        string? SubmittedBy,
        DateTime? ReportDate,
        DateTimeOffset? SubmittedAt,
        DateTimeOffset UpdatedAt,
        int OpenFindings);

    public sealed record AuditorReportDetailResponse(
        Guid Id,
        string OrganizationName,
        string OrganizationCvr,
        string? ReportNumber,
        string? CustomerName,
        string? CustomerEmail,
        string? CustomerPhone,
        string? CustomerAddress,
        string? CustomerContactPerson,
        string? DestinationAddress,
        string? DestinationZipCode,
        string? DestinationCity,
        string Status,
        string JobType,
        DateTime? ReportDate,
        string? TaskDescription,
        string? CustomerObservations,
        string? TechnicalObservations,
        string? Remarks,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? SubmittedAt,
        string? SubmittedBy,
        IReadOnlyList<AuditorInstallationResponse> Installations,
        IReadOnlyList<AuditorPersonResponse> AssignedUsers,
        IReadOnlyList<AuditorWorksheetResponse> Worksheets,
        IReadOnlyList<AuditorFindingResponse> Findings,
        IReadOnlyList<AuditorEventResponse> AuditEvents,
        IReadOnlyList<ImageInfoResponse> Images);

    public sealed record AuditorInstallationResponse(
        Guid Id,
        string Name,
        IReadOnlyList<AuditorControlCategoryResponse> Categories);

    public sealed record AuditorControlCategoryResponse(
        Guid Id,
        string Name,
        bool IsIrrelevant,
        IReadOnlyList<AuditorControlPointResponse> ControlPoints);

    public sealed record AuditorControlPointResponse(Guid Id, string Name, bool IsRequired, bool IsChecked);
    public sealed record AuditorPersonResponse(Guid Id, string DisplayName, string? Email);
    public sealed record AuditorWorksheetResponse(Guid Id, DateTime WorkDate, decimal HoursWorked, Guid UserId, string UserName);

    public sealed record AuditorFindingResponse(
        Guid Id,
        Guid AssignmentId,
        string Category,
        string Description,
        string? Reference,
        DateTimeOffset? DueAt,
        string Status,
        string? CompanyEvidence,
        DateTimeOffset? CompanyEvidenceAt,
        string? CreatedBy,
        string? CompanyEvidenceBy,
        string? VerifiedBy,
        DateTimeOffset? VerifiedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record AuditorEventResponse(Guid Id, string EventType, string? DetailsJson, DateTimeOffset CreatedAt, string? ActorName);

    public sealed record CreateAuditorFindingRequest(string Category, string Description, string? Reference, DateTimeOffset? DueAt);
    public sealed record UpdateAuditorFindingRequest(string Status, string? Description, DateTimeOffset? DueAt);
    public sealed record AddAuditorFindingEvidenceRequest(string Evidence);

    public sealed record CreateAuditorAssignmentRequest(
        Guid AuditorUserId,
        Guid TargetOrganizationId,
        string AuthorizationArea,
        DateTimeOffset? ActiveFrom,
        DateTimeOffset? ActiveUntil);

    public sealed record UpdateAuditorAssignmentRequest(bool IsActive, DateTimeOffset? ActiveUntil);

    public sealed record AuditorAssignmentAdminResponse(
        Guid Id,
        Guid AuditorUserId,
        string AuditorName,
        string AuditorEmail,
        Guid ControlOrganizationId,
        string ControlOrganizationName,
        Guid TargetOrganizationId,
        string TargetOrganizationName,
        string TargetOrganizationCvr,
        string AuthorizationArea,
        DateTimeOffset ActiveFrom,
        DateTimeOffset? ActiveUntil,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed class AuditorReportDbRow
    {
        public Guid Id { get; set; }
        public string? ReportNumber { get; set; }
        public string? CustomerName { get; set; }
        public string? Address { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? InstallationTypes { get; set; }
        public decimal? TotalHours { get; set; }
        public string? AssignedUsers { get; set; }
        public string? SubmittedBy { get; set; }
        public DateTime? ReportDate { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int OpenFindings { get; set; }
        public int TotalCount { get; set; }
    }

    private sealed class AuditorReportDetailDbRow
    {
        public Guid Id { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string OrganizationCvr { get; set; } = string.Empty;
        public string? ReportNumber { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CustomerContactPerson { get; set; }
        public string? DestinationAddress { get; set; }
        public string? DestinationZipCode { get; set; }
        public string? DestinationCity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
        public DateTime? ReportDate { get; set; }
        public string? TaskDescription { get; set; }
        public string? CustomerObservations { get; set; }
        public string? TechnicalObservations { get; set; }
        public string? Remarks { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string? SubmittedBy { get; set; }
    }

    private sealed class AuditorControlPointDbRow
    {
        public Guid InstallationId { get; set; }
        public int InstallationSortOrder { get; set; }
        public string InstallationTypeName { get; set; } = string.Empty;
        public Guid? CategoryId { get; set; }
        public int? CategorySortOrder { get; set; }
        public bool? IsIrrelevant { get; set; }
        public string? CategoryName { get; set; }
        public Guid? ControlPointId { get; set; }
        public int? ControlPointSortOrder { get; set; }
        public bool? IsRequired { get; set; }
        public bool? IsChecked { get; set; }
        public string? ControlPointName { get; set; }
    }

    private sealed record AuditorAssignmentRow(
        Guid Id,
        Guid AuditorUserId,
        Guid ControlOrganizationId,
        Guid TargetOrganizationId,
        string AuthorizationArea,
        DateTimeOffset ActiveFrom,
        DateTimeOffset? ActiveUntil);

    private sealed record AuditorFindingTargetRow(Guid AssignmentId, Guid TargetOrganizationId, Guid JobReportId, string Status);
    private sealed record AssignmentAuditorRow(Guid Id, Guid OrganizationId, string DisplayName, string Email, string Role);
    private sealed record AssignmentTargetRow(Guid TargetOrganizationId, DateTimeOffset ActiveFrom);
}
