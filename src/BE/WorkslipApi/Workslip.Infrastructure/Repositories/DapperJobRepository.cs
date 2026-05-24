using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperJobRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy) : IJobRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<JobReportResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.create", token => CreateAsyncCoreAsync(request, token), cancellationToken);

    private async Task<JobReportResponse> CreateAsyncCoreAsync(CreateJobRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var reportId = Guid.NewGuid();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.JobReports (
                Id, OrganizationId, CustomerId, ReportNumber, Status, CustomerName, CustomerAddress, CustomerEmail, ContactPerson, Phone,
                ReportDate, TaskDescription, CustomerObservations, TechnicalObservations, InstallationTypesJson, WorkKind, CustomWorkKind,
                Remarks, ClosureFlagsJson, PayloadJson, CreatedAt, UpdatedAt, SubmittedAt
            )
            values (
                @Id, @OrganizationId, @CustomerId, @ReportNumber, @Status, @CustomerName, @CustomerAddress, @CustomerEmail, @ContactPerson, @Phone,
                @ReportDate, @TaskDescription, @CustomerObservations, @TechnicalObservations, @InstallationTypesJson, @WorkKind, @CustomWorkKind,
                @Remarks, @ClosureFlagsJson, @PayloadJson, @CreatedAt, @UpdatedAt, null
            );
            """,
            new
            {
                Id = reportId,
                request.OrganizationId,
                request.CustomerId,
                request.ReportNumber,
                Status = JobStatus.Draft.ToString(),
                request.CustomerName,
                request.CustomerAddress,
                request.CustomerEmail,
                request.ContactPerson,
                request.Phone,
                ReportDate = ToDateTime(request.ReportDate),
                request.TaskDescription,
                request.CustomerObservations,
                request.TechnicalObservations,
                InstallationTypesJson = ToJson(request.InstallationTypes),
                request.WorkKind,
                request.CustomWorkKind,
                request.Remarks,
                ClosureFlagsJson = ToJson(request.ClosureFlags),
                PayloadJson = request.Payload?.ToJsonString(JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        await ReplaceControlInstallationTypesAsync(connection, transaction, reportId, request.ControlInstallationTypes, now, cancellationToken);
        await InsertEventAsync(connection, transaction, reportId, null, "created", null, ToJsonNode(new { reportId }), now, cancellationToken);

        transaction.Commit();
        return (await GetAsync(reportId, cancellationToken))!;
    }

    public Task<IReadOnlyList<JobListItemResponse>> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.list", token => ListAsyncCoreAsync(query, token), cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> ListAsyncCoreAsync(JobQuery query, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<JobReportRow>(new CommandDefinition(
            """
            select *
            from dbo.JobReports
            where (@OrganizationId is null or OrganizationId = @OrganizationId)
              and (@Status is null or Status = @Status)
            order by UpdatedAt desc
            offset @Offset rows fetch next @Limit rows only;
            """,
            new
            {
                query.OrganizationId,
                Status = query.Status?.ToString(),
                query.Limit,
                query.Offset
            },
            cancellationToken: cancellationToken));

         return rows.Select(row => new JobListItemResponse(
             row.Id,
             row.OrganizationId,
             row.CustomerId,
             row.ReportNumber,
             ParseStatus(row.Status),
             row.CustomerName,
             row.CustomerAddress,
             row.CustomerEmail,
             ToDateOnly(row.ReportDate),
             FromJsonList(row.InstallationTypesJson),
             row.WorkKind,
             row.CustomWorkKind,
             row.CreatedAt,
             row.UpdatedAt,
             row.SubmittedAt,
             row.AssignedUserId.HasValue ? new AssignedUserResponse(row.AssignedUserId.Value, "") : null)).ToArray();
    }

    public Task<JobReportResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.get", token => GetAsyncCoreAsync(id, token), cancellationToken);

    private async Task<JobReportResponse?> GetAsyncCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id = @Id;",
            new { Id = id },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var subcategories = await connection.QueryAsync<JobControlSubcategoryRow>(new CommandDefinition(
            "select * from dbo.JobControlSubcategoryDecisions where ReportId = @Id order by InstallationTypeId, SubcategoryId;",
            new { Id = id },
            cancellationToken: cancellationToken));

        var checks = await connection.QueryAsync<JobControlCheckRow>(new CommandDefinition(
            "select * from dbo.JobControlChecks where ReportId = @Id order by InstallationTypeId, SubcategoryId, ItemId;",
            new { Id = id },
            cancellationToken: cancellationToken));

        var links = await LoadLinksAsync(connection, id, cancellationToken);

        return ToResponse(row, subcategories, checks, links);
    }

    public Task<JobReportResponse?> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.update", token => UpdateAsyncCoreAsync(id, request, token), cancellationToken);

    private async Task<JobReportResponse?> UpdateAsyncCoreAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id = @Id;",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (existing is null || !JobStatusPolicy.CanEdit(ParseStatus(existing.Status)))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.JobReports
            set CustomerId = @CustomerId,
                ReportNumber = coalesce(@ReportNumber, ReportNumber),
                CustomerName = coalesce(@CustomerName, CustomerName),
                CustomerAddress = coalesce(@CustomerAddress, CustomerAddress),
                CustomerEmail = @CustomerEmail,
                ContactPerson = @ContactPerson,
                Phone = @Phone,
                ReportDate = coalesce(@ReportDate, ReportDate),
                TaskDescription = coalesce(@TaskDescription, TaskDescription),
                CustomerObservations = @CustomerObservations,
                TechnicalObservations = @TechnicalObservations,
                InstallationTypesJson = coalesce(@InstallationTypesJson, InstallationTypesJson),
                WorkKind = coalesce(@WorkKind, WorkKind),
                CustomWorkKind = @CustomWorkKind,
                Remarks = @Remarks,
                ClosureFlagsJson = coalesce(@ClosureFlagsJson, ClosureFlagsJson),
                PayloadJson = coalesce(@PayloadJson, PayloadJson),
                UpdatedAt = @UpdatedAt
            where Id = @Id;
            """,
            new
            {
                Id = id,
                request.CustomerId,
                request.ReportNumber,
                request.CustomerName,
                request.CustomerAddress,
                request.CustomerEmail,
                request.ContactPerson,
                request.Phone,
                ReportDate = ToDateTime(request.ReportDate),
                request.TaskDescription,
                request.CustomerObservations,
                request.TechnicalObservations,
                InstallationTypesJson = request.InstallationTypes is null ? null : ToJson(request.InstallationTypes),
                request.WorkKind,
                request.CustomWorkKind,
                request.Remarks,
                ClosureFlagsJson = request.ClosureFlags is null ? null : ToJson(request.ClosureFlags),
                PayloadJson = request.Payload?.ToJsonString(JsonOptions),
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        if (request.ControlInstallationTypes is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "delete from dbo.JobControlSubcategoryDecisions where ReportId = @Id;",
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken));
            await ReplaceControlInstallationTypesAsync(connection, transaction, id, request.ControlInstallationTypes, now, cancellationToken);
        }

        await InsertEventAsync(connection, transaction, id, null, "updated", ToJsonNode(existing), ToJsonNode(request), now, cancellationToken);
        transaction.Commit();

        return await GetAsync(id, cancellationToken);
    }

    public Task<JobReportResponse?> TransitionAsync(Guid id, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.transition", token => TransitionAsyncCoreAsync(id, nextStatus, actorId, token), cancellationToken);

    private async Task<JobReportResponse?> TransitionAsyncCoreAsync(Guid id, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id = @Id;",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (existing is null)
        {
            return null;
        }

        var currentStatus = ParseStatus(existing.Status);
        if (!JobStatusPolicy.CanTransition(currentStatus, nextStatus))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.JobReports
            set Status = @Status,
                UpdatedAt = @UpdatedAt,
                SubmittedAt = case when @Status = 'Submitted' then @UpdatedAt else SubmittedAt end
            where Id = @Id;
            """,
            new { Id = id, Status = nextStatus.ToString(), UpdatedAt = now },
            transaction,
            cancellationToken: cancellationToken));

        await InsertEventAsync(connection, transaction, id, actorId, nextStatus.ToString().ToLowerInvariant(), ToJsonNode(existing), ToJsonNode(new { status = nextStatus.ToString() }), now, cancellationToken);
        transaction.Commit();

        return await GetAsync(id, cancellationToken);
    }

     public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
         retryPolicy.ExecuteAsync("jobs.delete", token => DeleteAsyncCoreAsync(id, token), cancellationToken);

     private async Task<bool> DeleteAsyncCoreAsync(Guid id, CancellationToken cancellationToken)
     {
         using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
         using var transaction = connection.BeginTransaction();

         var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
             "select * from dbo.JobReports where Id = @Id;",
             new { Id = id },
             transaction,
             cancellationToken: cancellationToken));

         if (existing is null)
         {
             return false;
         }

         await connection.ExecuteAsync(new CommandDefinition(
             "delete from dbo.JobReportLinks where SourceReportId = @Id or TargetReportId = @Id;",
             new { Id = id },
             transaction,
             cancellationToken: cancellationToken));

         await InsertEventAsync(connection, transaction, id, null, "deleted", ToJsonNode(existing), null, DateTimeOffset.UtcNow, cancellationToken);

         await connection.ExecuteAsync(new CommandDefinition(
             "delete from dbo.JobReports where Id = @Id;",
             new { Id = id },
             transaction,
             cancellationToken: cancellationToken));

         transaction.Commit();
         return true;
     }

     public Task<JobReportResponse?> AssignAsync(Guid jobId, Guid? userId, Guid? actorId, CancellationToken cancellationToken) =>
         retryPolicy.ExecuteAsync("jobs.assign", token => AssignAsyncCoreAsync(jobId, userId, actorId, token), cancellationToken);

     private async Task<JobReportResponse?> AssignAsyncCoreAsync(Guid jobId, Guid? userId, Guid? actorId, CancellationToken cancellationToken)
     {
         using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
         using var transaction = connection.BeginTransaction();

         // Get the existing job
         var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
             "select * from dbo.JobReports where Id = @Id;",
             new { Id = jobId },
             transaction,
             cancellationToken: cancellationToken));

         if (existing is null)
         {
             return null;
         }

         // If userId is provided, validate that the user exists, is active, and belongs to the same organization
         if (userId.HasValue)
         {
             var user = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(
                 "select Id, DisplayName from dbo.Users where Id = @UserId and IsActive = 1 and OrganizationId = @OrgId;",
                 new { UserId = userId.Value, OrgId = existing.OrganizationId },
                 transaction,
                 cancellationToken: cancellationToken));

             if (user is null)
             {
                 return null; // User doesn't exist, is inactive, or belongs to different organization
             }
         }

         // Check if assignment is actually changing
         if (existing.AssignedUserId == userId)
         {
             // No change needed, return current job
             return await GetAsync(jobId, cancellationToken);
         }

         var now = DateTimeOffset.UtcNow;
         
         // Update the assigned user
         await connection.ExecuteAsync(new CommandDefinition(
             "update dbo.JobReports set AssignedUserId = @AssignedUserId, UpdatedAt = @UpdatedAt where Id = @Id;",
             new { Id = jobId, AssignedUserId = userId, UpdatedAt = now },
             transaction,
             cancellationToken: cancellationToken));

         // Determine event type based on whether this is initial assignment or reassignment
         string eventType = existing.AssignedUserId.HasValue ? "reassigned" : "assigned";
         
         // Create before/after snapshots for the event
         var before = JsonNode.Parse(JsonSerializer.Serialize(new { AssignedUserId = existing.AssignedUserId }, JsonOptions))?.AsObject();
         var after = JsonNode.Parse(JsonSerializer.Serialize(new { AssignedUserId = userId }, JsonOptions))?.AsObject();

         await InsertEventAsync(connection, transaction, jobId, actorId, eventType, before, after, now, cancellationToken);
         
         transaction.Commit();
         return await GetAsync(jobId, cancellationToken);
     }

    private static async Task ReplaceControlInstallationTypesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid reportId,
        IReadOnlyList<ControlInstallationTypeRequest> installationTypes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var installationType in installationTypes)
        {
            foreach (var subcategory in installationType.Subcategories)
            {
                var subcategoryDecisionId = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into dbo.JobControlSubcategoryDecisions (Id, ReportId, InstallationTypeId, SubcategoryId, CreatedAt, UpdatedAt)
                    values (@Id, @ReportId, @InstallationTypeId, @SubcategoryId, @CreatedAt, @UpdatedAt);
                    """,
                    new
                    {
                        Id = subcategoryDecisionId,
                        ReportId = reportId,
                        InstallationTypeId = installationType.InstallationTypeId,
                        subcategory.SubcategoryId,
                        CreatedAt = now,
                        UpdatedAt = now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                foreach (var check in subcategory.ControlChecks)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into dbo.JobControlChecks (Id, ReportId, SubcategoryDecisionId, InstallationTypeId, SubcategoryId, ItemId, Checked, Note, CreatedAt, UpdatedAt)
                        values (@Id, @ReportId, @SubcategoryDecisionId, @InstallationTypeId, @SubcategoryId, @ItemId, @Checked, @Note, @CreatedAt, @UpdatedAt);
                        """,
                        new
                        {
                            Id = Guid.NewGuid(),
                            ReportId = reportId,
                            SubcategoryDecisionId = subcategoryDecisionId,
                            InstallationTypeId = installationType.InstallationTypeId,
                            subcategory.SubcategoryId,
                            check.ItemId,
                            check.Checked,
                            check.Note,
                            CreatedAt = now,
                            UpdatedAt = now
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                }
            }
        }
    }

    private static async Task InsertEventAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid reportId,
        Guid? actorId,
        string eventType,
        JsonObject? before,
        JsonObject? after,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.JobEvents (Id, ReportId, ActorId, EventType, BeforeJson, AfterJson, CreatedAt)
            values (@Id, @ReportId, @ActorId, @EventType, @BeforeJson, @AfterJson, @CreatedAt);
            """,
            new
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                ActorId = actorId,
                EventType = eventType,
                BeforeJson = before?.ToJsonString(JsonOptions),
                AfterJson = after?.ToJsonString(JsonOptions),
                CreatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<IReadOnlyList<JobLinkInfoResponse>> LoadLinksAsync(
        IDbConnection connection,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var links = await connection.QueryAsync<JobReportLinkRow>(new CommandDefinition(
            "select * from dbo.JobReportLinks where SourceReportId = @Id or TargetReportId = @Id;",
            new { Id = reportId },
            cancellationToken: cancellationToken));

        var linkedIds = links.Select(link =>
            link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId).Distinct().ToArray();

        if (linkedIds.Length == 0)
            return [];

        var linkedReports = (await connection.QueryAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id in @Ids;",
            new { Ids = linkedIds },
            cancellationToken: cancellationToken)))
            .ToDictionary(r => r.Id);

        return links.Select(link =>
        {
            var linkedId = link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId;
            var linked = linkedReports.GetValueOrDefault(linkedId);
            return new JobLinkInfoResponse(
                linkedId,
                linked?.ReportNumber ?? "",
                linked?.CustomerName ?? "",
                linked?.Status ?? "",
                link.LinkType);
        }).ToArray();
    }

    private static JobReportResponse ToResponse(
        JobReportRow row,
        IEnumerable<JobControlSubcategoryRow> subcategories,
        IEnumerable<JobControlCheckRow> checks,
        IReadOnlyList<JobLinkInfoResponse> links)
    {
        var checksBySubcategory = checks
            .GroupBy(check => check.SubcategoryDecisionId)
            .ToDictionary(group => group.Key, group => group.Select(check => new ControlCheckResponse(
                check.Id,
                check.ItemId,
                check.Checked,
                check.Note,
                check.CreatedAt,
                check.UpdatedAt)).ToArray() as IReadOnlyList<ControlCheckResponse>);

        var subcategoryResponses = subcategories.Select(subcategory => new ControlSubcategoryResponse(
            subcategory.Id,
            subcategory.InstallationTypeId,
            subcategory.SubcategoryId,
            checksBySubcategory.TryGetValue(subcategory.Id, out var subcategoryChecks) ? subcategoryChecks : [],
            subcategory.CreatedAt,
            subcategory.UpdatedAt)).ToArray();

        var installationTypeResponses = subcategoryResponses
            .GroupBy(subcategory => subcategory.InstallationTypeId)
            .Select(group => new ControlInstallationTypeResponse(group.Key, group.ToArray()))
            .ToArray();

         return new(
             row.Id,
             row.OrganizationId,
             row.CustomerId,
             row.ReportNumber,
             ParseStatus(row.Status),
             row.CustomerName,
             row.CustomerAddress,
             row.CustomerEmail,
             row.ContactPerson,
             row.Phone,
             ToDateOnly(row.ReportDate),
             row.TaskDescription,
             row.CustomerObservations,
             row.TechnicalObservations,
             FromJsonList(row.InstallationTypesJson),
             row.WorkKind,
             row.CustomWorkKind,
             row.Remarks,
             FromJsonList(row.ClosureFlagsJson),
             string.IsNullOrWhiteSpace(row.PayloadJson) ? null : JsonNode.Parse(row.PayloadJson) as JsonObject,
             installationTypeResponses,
             links,
             row.CreatedAt,
             row.UpdatedAt,
             row.SubmittedAt,
             row.AssignedUserId.HasValue ? new AssignedUserResponse(row.AssignedUserId.Value, "") : null);
    }

    private static JobStatus ParseStatus(string status) => Enum.Parse<JobStatus>(status, ignoreCase: true);

    private static DateTime? ToDateTime(DateOnly? value) =>
        value is null ? null : value.Value.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private static IReadOnlyList<string> FromJsonList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
}
