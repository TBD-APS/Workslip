using System.Data;
using System.Text.Json;
using Dapper;
using Workslip.Application.Auditing;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlAuditorRepository(ISqlConnectionFactory connectionFactory) : IAuditorRepository
{
    private const string AuthorizationAreaVvs = "VVS";

    public async Task<IReadOnlyList<AuditorOrganizationSummaryResponse>> ListOrganizationsAsync(
        Guid auditorUserId,
        Guid controlOrganizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
            WHERE a.AuditorUserId = @AuditorUserId
              AND a.ControlOrganizationId = @ControlOrganizationId
              AND a.AuthorizationArea = @AuthorizationArea
              AND a.IsActive = 1
              AND a.ActiveFrom <= @Now
              AND (a.ActiveUntil IS NULL OR a.ActiveUntil > @Now)
            ORDER BY o.Name ASC, o.Id ASC;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditorOrganizationSummaryResponse>(new CommandDefinition(
            sql,
            new { AuditorUserId = auditorUserId, ControlOrganizationId = controlOrganizationId, AuthorizationArea = AuthorizationAreaVvs, Now = now },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<AuditorAssignmentGrant?> GetActiveAssignmentAsync(
        Guid auditorUserId,
        Guid controlOrganizationId,
        Guid targetOrganizationId,
        DateTimeOffset now,
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
              AND ActiveFrom <= @Now
              AND (ActiveUntil IS NULL OR ActiveUntil > @Now)
            ORDER BY ActiveFrom DESC, CreatedAt DESC;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AuditorAssignmentGrant>(new CommandDefinition(
            sql,
            new
            {
                AuditorUserId = auditorUserId,
                ControlOrganizationId = controlOrganizationId,
                TargetOrganizationId = targetOrganizationId,
                AuthorizationArea = AuthorizationAreaVvs,
                Now = now
            },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> IsVisibleJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
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
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<AuditorReportListResponse> ListReportsAsync(
        Guid organizationId,
        string? search,
        string? installationType,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
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

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<ReportListDbRow>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, Search = search, InstallationType = installationType, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken))).AsList();
        var items = rows.Select(row => new AuditorReportListItemResponse(
            row.Id, row.ReportNumber, row.CustomerName, row.Address, row.Status, row.InstallationTypes,
            row.TotalHours, row.AssignedUsers, row.SubmittedBy, row.ReportDate, row.SubmittedAt, row.UpdatedAt,
            row.OpenFindings)).ToArray();
        return new AuditorReportListResponse(items, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<AuditorReportDetailResponse?> GetReportAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        const string reportSql = """
            SELECT TOP (1)
                jr.Id, jr.ReportNumber, jr.CustomerName, jr.CustomerEmail, jr.CustomerPhone,
                jr.CustomerAddress, jr.CustomerContactPerson, jr.DestinationAddress, jr.DestinationZipCode,
                jr.DestinationCity, jr.Status, jr.JobType, jr.ReportDate, jr.TaskDescription,
                jr.CustomerObservations, jr.TechnicalObservations, jr.Remarks, jr.CreatedAt, jr.UpdatedAt,
                jr.SubmittedAt, submittedBy.DisplayName AS SubmittedBy,
                o.Name AS OrganizationName, o.Cvr AS OrganizationCvr
            FROM dbo.JobReports jr
            INNER JOIN dbo.Organizations o ON o.Id = jr.OrganizationId
            LEFT JOIN dbo.Users submittedBy
              ON submittedBy.Id = jr.SubmittedByUserId
             AND submittedBy.OrganizationId = jr.OrganizationId
            WHERE jr.OrganizationId = @OrganizationId
              AND jr.Id = @JobId;
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var report = await connection.QuerySingleOrDefaultAsync<ReportDetailDbRow>(new CommandDefinition(
            reportSql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken));
        if (report is null) return null;

        var installationRows = (await connection.QueryAsync<ControlPointDbRow>(new CommandDefinition(
            InstallationSql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken))).AsList();

        var installations = installationRows
            .GroupBy(row => new { row.InstallationId, row.InstallationTypeName, row.InstallationSortOrder })
            .OrderBy(group => group.Key.InstallationSortOrder)
            .Select(group => new AuditorInstallationResponse(
                group.Key.InstallationId,
                group.Key.InstallationTypeName,
                group.Where(row => row.CategoryId is not null)
                    .GroupBy(row => new { row.CategoryId, row.CategoryName, row.CategorySortOrder, row.IsIrrelevant })
                    .OrderBy(category => category.Key.CategorySortOrder)
                    .Select(category => new AuditorControlCategoryResponse(
                        category.Key.CategoryId!.Value,
                        category.Key.CategoryName ?? string.Empty,
                        category.Key.IsIrrelevant ?? false,
                        category.Where(row => row.ControlPointId is not null)
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

        return new AuditorReportDetailResponse(
            report.Id, report.OrganizationName, report.OrganizationCvr, report.ReportNumber, report.CustomerName,
            report.CustomerEmail, report.CustomerPhone, report.CustomerAddress, report.CustomerContactPerson,
            report.DestinationAddress, report.DestinationZipCode, report.DestinationCity, report.Status,
            report.JobType, report.ReportDate, report.TaskDescription, report.CustomerObservations,
            report.TechnicalObservations, report.Remarks, report.CreatedAt, report.UpdatedAt, report.SubmittedAt,
            report.SubmittedBy, installations, assigned, worksheets, [], [], []);
    }

    public async Task<IReadOnlyList<AuditorFindingResponse>> ListFindingsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await ListFindingsCoreAsync(connection, organizationId, jobId, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditorEventResponse>> ListEventsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT e.Id, e.EventType, e.DetailsJson, e.CreatedAt, u.DisplayName AS ActorName
            FROM dbo.AuditorEvents e
            LEFT JOIN dbo.Users u ON u.Id = e.ActorUserId
            WHERE e.TargetOrganizationId = @OrganizationId
              AND e.JobReportId = @JobId
            ORDER BY e.CreatedAt DESC, e.Id DESC;
            """;
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditorEventResponse>(new CommandDefinition(
            sql,
            new { OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task RecordReportOpenedAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid actorUserId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await WriteEventAsync(connection, null, assignmentId, organizationId, jobId, null, actorUserId, "ReportOpened", null, cancellationToken);
    }

    public async Task<AuditorFindingResponse?> CreateFindingAsync(
        Guid assignmentId,
        Guid organizationId,
        Guid jobId,
        Guid actorUserId,
        CreateAuditorFindingCommand command,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var findingId = Guid.NewGuid();
        const string sql = """
            INSERT INTO dbo.AuditorFindings
            (
                Id, AssignmentId, TargetOrganizationId, JobReportId, Category, Description, Reference,
                DueAt, Status, CreatedByAuditorUserId, CreatedAt, UpdatedAt
            )
            VALUES
            (
                @Id, @AssignmentId, @TargetOrganizationId, @JobReportId, @Category, @Description, @Reference,
                @DueAt, N'Open', @CreatedByAuditorUserId, @CreatedAt, @UpdatedAt
            );
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = findingId,
            AssignmentId = assignmentId,
            TargetOrganizationId = organizationId,
            JobReportId = jobId,
            command.Category,
            command.Description,
            command.Reference,
            command.DueAt,
            CreatedByAuditorUserId = actorUserId,
            CreatedAt = command.Now,
            UpdatedAt = command.Now
        }, transaction, cancellationToken: cancellationToken));
        await WriteEventAsync(
            connection, transaction, assignmentId, organizationId, jobId, findingId, actorUserId,
            "FindingCreated", JsonSerializer.Serialize(new { command.Category, command.DueAt }), cancellationToken);
        transaction.Commit();
        return await GetFindingCoreAsync(connection, findingId, cancellationToken);
    }

    public async Task<bool> FindingBelongsToJobAsync(Guid findingId, Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.AuditorFindings
            WHERE Id = @FindingId AND TargetOrganizationId = @OrganizationId AND JobReportId = @JobId;
            """;
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { FindingId = findingId, OrganizationId = organizationId, JobId = jobId },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<AuditorFindingResponse?> UpdateFindingAsync(
        Guid assignmentId,
        Guid organizationId,
        Guid jobId,
        Guid findingId,
        Guid actorUserId,
        UpdateAuditorFindingCommand command,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        const string sql = """
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
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            FindingId = findingId,
            OrganizationId = organizationId,
            JobId = jobId,
            command.Description,
            command.DueAt,
            command.Status,
            ActorUserId = actorUserId,
            UpdatedAt = command.Now
        }, transaction, cancellationToken: cancellationToken));
        if (affected == 0)
        {
            transaction.Rollback();
            return null;
        }
        await WriteEventAsync(
            connection, transaction, assignmentId, organizationId, jobId, findingId, actorUserId,
            command.Status == "Closed" ? "FindingClosed" : "FindingUpdated",
            JsonSerializer.Serialize(new { command.Status, command.DueAt }), cancellationToken);
        transaction.Commit();
        return await GetFindingCoreAsync(connection, findingId, cancellationToken);
    }

    public async Task<AuditorFindingTarget?> GetFindingTargetAsync(Guid findingId, Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) AssignmentId, TargetOrganizationId, JobReportId, Status
            FROM dbo.AuditorFindings
            WHERE Id = @FindingId AND TargetOrganizationId = @OrganizationId;
            """;
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AuditorFindingTarget>(new CommandDefinition(
            sql,
            new { FindingId = findingId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));
    }

    public async Task<AuditorFindingResponse?> AddCompanyEvidenceAsync(
        AuditorFindingTarget target,
        Guid findingId,
        Guid actorUserId,
        string evidence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        const string sql = """
            UPDATE dbo.AuditorFindings
            SET CompanyEvidence = @Evidence,
                CompanyEvidenceByUserId = @ActorUserId,
                CompanyEvidenceAt = @UpdatedAt,
                Status = N'ReadyForVerification',
                UpdatedAt = @UpdatedAt
            WHERE Id = @FindingId
              AND TargetOrganizationId = @OrganizationId
              AND Status <> N'Closed';
            """;
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            FindingId = findingId,
            OrganizationId = target.TargetOrganizationId,
            Evidence = evidence,
            ActorUserId = actorUserId,
            UpdatedAt = now
        }, transaction, cancellationToken: cancellationToken));
        if (affected == 0)
        {
            transaction.Rollback();
            return null;
        }
        await WriteEventAsync(
            connection, transaction, target.AssignmentId, target.TargetOrganizationId, target.JobReportId,
            findingId, actorUserId, "CompanyEvidenceAdded", null, cancellationToken);
        transaction.Commit();
        return await GetFindingCoreAsync(connection, findingId, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditorAssignmentAdminResponse>> ListAssignmentsAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditorAssignmentAdminResponse>(new CommandDefinition(AssignmentSelectSql + " ORDER BY a.IsActive DESC, target.Name ASC, u.DisplayName ASC, a.CreatedAt DESC;", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<AuditorUserSubject?> GetAuditorUserAsync(Guid auditorUserId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP (1) Id, OrganizationId, DisplayName, Email, Role FROM dbo.Users WHERE Id = @AuditorUserId;";
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AuditorUserSubject>(new CommandDefinition(
            sql,
            new { AuditorUserId = auditorUserId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Organizations WHERE Id = @OrganizationId;",
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> ActiveAssignmentExistsAsync(Guid auditorUserId, Guid targetOrganizationId, string authorizationArea, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.AuditorAssignments
            WHERE AuditorUserId = @AuditorUserId
              AND TargetOrganizationId = @TargetOrganizationId
              AND AuthorizationArea = @AuthorizationArea
              AND IsActive = 1
              AND ActiveFrom <= @Now
              AND (ActiveUntil IS NULL OR ActiveUntil > @Now);
            """;
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { AuditorUserId = auditorUserId, TargetOrganizationId = targetOrganizationId, AuthorizationArea = authorizationArea, Now = now },
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<AuditorAssignmentAdminResponse?> CreateAssignmentAsync(
        Guid actorUserId,
        AuditorUserSubject auditor,
        CreateAuditorAssignmentCommand command,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var id = Guid.NewGuid();
        const string sql = """
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
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            command.AuditorUserId,
            ControlOrganizationId = auditor.OrganizationId,
            command.TargetOrganizationId,
            command.AuthorizationArea,
            command.ActiveFrom,
            command.ActiveUntil,
            ActorUserId = actorUserId,
            CreatedAt = command.Now,
            UpdatedAt = command.Now
        }, transaction, cancellationToken: cancellationToken));
        await WriteEventAsync(
            connection, transaction, id, command.TargetOrganizationId, null, null, actorUserId,
            "AssignmentCreated",
            JsonSerializer.Serialize(new { command.AuditorUserId, controlOrganizationId = auditor.OrganizationId, command.AuthorizationArea, command.ActiveFrom, command.ActiveUntil }),
            cancellationToken);
        transaction.Commit();
        return await GetAssignmentCoreAsync(connection, id, cancellationToken);
    }

    public async Task<AuditorAssignmentTarget?> GetAssignmentTargetAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP (1) TargetOrganizationId, ActiveFrom FROM dbo.AuditorAssignments WHERE Id = @AssignmentId;";
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AuditorAssignmentTarget>(new CommandDefinition(
            sql,
            new { AssignmentId = assignmentId },
            cancellationToken: cancellationToken));
    }

    public async Task<AuditorAssignmentAdminResponse?> UpdateAssignmentAsync(
        Guid assignmentId,
        Guid actorUserId,
        bool isActive,
        DateTimeOffset? activeUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var target = await connection.QuerySingleOrDefaultAsync<AuditorAssignmentTarget>(new CommandDefinition(
            "SELECT TOP (1) TargetOrganizationId, ActiveFrom FROM dbo.AuditorAssignments WHERE Id = @AssignmentId;",
            new { AssignmentId = assignmentId }, transaction, cancellationToken: cancellationToken));
        if (target is null)
        {
            transaction.Rollback();
            return null;
        }
        const string sql = """
            UPDATE dbo.AuditorAssignments
            SET IsActive = @IsActive,
                ActiveUntil = @ActiveUntil,
                UpdatedByUserId = @ActorUserId,
                UpdatedAt = @UpdatedAt
            WHERE Id = @AssignmentId;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            AssignmentId = assignmentId,
            IsActive = isActive,
            ActiveUntil = activeUntil,
            ActorUserId = actorUserId,
            UpdatedAt = now
        }, transaction, cancellationToken: cancellationToken));
        await WriteEventAsync(
            connection, transaction, assignmentId, target.TargetOrganizationId, null, null, actorUserId,
            isActive ? "AssignmentUpdated" : "AssignmentDeactivated",
            JsonSerializer.Serialize(new { isActive, activeUntil }), cancellationToken);
        transaction.Commit();
        return await GetAssignmentCoreAsync(connection, assignmentId, cancellationToken);
    }

    private static async Task<IReadOnlyList<AuditorFindingResponse>> ListFindingsCoreAsync(IDbConnection connection, Guid organizationId, Guid jobId, CancellationToken cancellationToken)
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

    private static async Task<AuditorFindingResponse?> GetFindingCoreAsync(IDbConnection connection, Guid findingId, CancellationToken cancellationToken)
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

    private static async Task<AuditorAssignmentAdminResponse?> GetAssignmentCoreAsync(IDbConnection connection, Guid assignmentId, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<AuditorAssignmentAdminResponse>(new CommandDefinition(
            AssignmentSelectSql + " WHERE a.Id = @AssignmentId;",
            new { AssignmentId = assignmentId },
            cancellationToken: cancellationToken));
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
        await connection.ExecuteAsync(new CommandDefinition(sql, new
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
        }, transaction, cancellationToken: cancellationToken));
    }

    private const string AssignmentSelectSql = """
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
        """;

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

    private sealed class ReportListDbRow
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

    private sealed class ReportDetailDbRow
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

    private sealed class ControlPointDbRow
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
}
