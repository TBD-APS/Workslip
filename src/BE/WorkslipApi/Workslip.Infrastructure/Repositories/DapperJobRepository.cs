using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperJobRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy, ICustomerRepository customerRepository) : IJobRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DeletionRetentionPeriod = TimeSpan.FromDays(30);

    public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.create", token => CreateAsyncCoreAsync(organizationId, request, assignedUserIds, actorId, token), cancellationToken);

    private async Task<JobReportResponse> CreateAsyncCoreAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var reportId = Guid.NewGuid();

        Guid? customerId = null;
        if (request.Customer?.Email is not null)
        {
            customerId = await customerRepository.UpsertByEmailAsync(organizationId, request.Customer, connection, transaction, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.JobReports (
                Id, OrganizationId, CustomerId, ReportNumber, Status,
                ReportDate, TaskDescription, CustomerObservations, TechnicalObservations, InstallationTypesJson, WorkKind, CustomWorkKind,
                Remarks, ClosureFlagsJson, PayloadJson, CreatedAt, UpdatedAt, SubmittedAt
            )
            values (
                @Id, @OrganizationId, @CustomerId, @ReportNumber, @Status,
                @ReportDate, @TaskDescription, @CustomerObservations, @TechnicalObservations, @InstallationTypesJson, @WorkKind, @CustomWorkKind,
                @Remarks, @ClosureFlagsJson, @PayloadJson, @CreatedAt, @UpdatedAt, null
            );
            """,
            new
            {
                Id = reportId,
                OrganizationId = organizationId,
                CustomerId = customerId,
                request.ReportNumber,
                Status = JobStatus.Draft.ToString(),
                ReportDate = ToDateTime(request.ReportDate),
                request.TaskDescription,
                request.CustomerObservations,
                request.TechnicalObservations,
                InstallationTypesJson = ToJson(request.InstallationTypes ?? []),
                WorkKind = NormalizeOptional(request.WorkKind),
                request.CustomWorkKind,
                request.Remarks,
                ClosureFlagsJson = ToJson(request.ClosureFlags ?? []),
                PayloadJson = request.Payload?.ToJsonString(JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        await ReplaceControlInstallationTypesAsync(connection, transaction, organizationId, reportId, request.ControlInstallationTypes, now, cancellationToken);
        var normalizedAssignedUserIds = NormalizeUserIds(assignedUserIds);
        await ReplaceAssignedUsersAsync(connection, transaction, organizationId, reportId, normalizedAssignedUserIds, actorId, now, cancellationToken);
        var assignedUsers = await LoadAssignedUsersByIdsAsync(connection, transaction, organizationId, normalizedAssignedUserIds, cancellationToken);
        await InsertEventAsync(connection, transaction, organizationId, reportId, actorId, "created", null, ToJsonNode(new { reportId, assignedUsers }), now, cancellationToken);

        transaction.Commit();
        return (await GetAsync(reportId, organizationId, cancellationToken))!;
    }

    public Task<IReadOnlyList<JobListItemResponse>> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.list", token => ListAsyncCoreAsync(query, token), cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> ListAsyncCoreAsync(JobQuery query, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<JobReportRow>(new CommandDefinition(
            """
            select report.Id, report.OrganizationId, report.CustomerId, report.ReportNumber, report.Status,
                   report.ReportDate, report.TaskDescription, report.CustomerObservations, report.TechnicalObservations,
                   report.InstallationTypesJson, report.WorkKind, report.CustomWorkKind, report.Remarks,
                   report.ClosureFlagsJson, report.PayloadJson, report.IsSoftDeleted,
                   report.CreatedAt, report.UpdatedAt, report.SubmittedAt, report.DeletionScheduledAt
            from dbo.JobReports report
            left join dbo.Customers c on c.Id = report.CustomerId
            where report.OrganizationId = @OrganizationId
              and (@Status is null or report.Status = @Status)
              and (@ReportNumberLike is null or report.ReportNumber like @ReportNumberLike escape '\')
              and (@CustomerNameLike is null or c.Name like @CustomerNameLike escape '\')
              and (@CustomerEmailLike is null or c.Email like @CustomerEmailLike escape '\')
              and (@CustomerAddressLike is null or c.Address like @CustomerAddressLike escape '\')
            order by report.UpdatedAt desc
            offset @Offset rows fetch next @Limit rows only;
            """,
            new
            {
                query.OrganizationId,
                Status = query.Status?.ToString(),
                CustomerNameLike = ToSearchLike(query.CustomerNameSearch),
                CustomerEmailLike = ToSearchLike(query.CustomerEmailSearch),
                CustomerAddressLike = ToSearchLike(query.CustomerAddressSearch),
                ReportNumberLike = ToSearchLike(query.ReportNumberSearch),
                query.Limit,
                query.Offset
            },
            cancellationToken: cancellationToken))).ToArray();

        var assignedUsersByReport = await LoadAssignedUsersAsync(connection, query.OrganizationId, rows.Select(row => row.Id), cancellationToken);
        var totalHoursByJob = await GetTotalHoursByJobAsync(connection, rows.Select(row => row.Id), cancellationToken);

        var customerIds = rows.Where(r => r.CustomerId.HasValue).Select(r => r.CustomerId!.Value).Distinct().ToArray();
        var customers = customerIds.Length > 0
            ? (await connection.QueryAsync<CustomerRow>(new CommandDefinition(
                "select * from dbo.Customers where OrganizationId = @OrganizationId and Id in @Ids;",
                new { OrganizationId = query.OrganizationId, Ids = customerIds },
                cancellationToken: cancellationToken))).ToDictionary(c => c.Id)
            : new Dictionary<Guid, CustomerRow>();

         return rows.Select(row =>
         {
             var customer = row.CustomerId is not null ? customers.GetValueOrDefault(row.CustomerId.Value) : null;
             return new JobListItemResponse(
                 row.Id,
                 row.OrganizationId,
                 customer is not null ? new CustomerInfo(customer.Id, customer.Name, customer.Address, customer.Email, customer.ContactPerson, customer.Phone) : null,
                 row.ReportNumber,
                 ParseStatus(row.Status),
                 ToDateOnly(row.ReportDate),
                 FromJsonList(row.InstallationTypesJson),
                 row.WorkKind,
                 row.CustomWorkKind,
                 row.CreatedAt,
                 row.UpdatedAt,
                 row.SubmittedAt,
                 assignedUsersByReport.GetValueOrDefault(row.Id) ?? [],
                 row.IsSoftDeleted,
                 row.DeletionScheduledAt,
                 totalHoursByJob.GetValueOrDefault(row.Id));
         }).ToArray();
    }

    public Task<JobReportResponse?> GetAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.get", token => GetAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> GetAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
            """
            select report.Id, report.OrganizationId, report.CustomerId, report.ReportNumber, report.Status,
                   report.ReportDate, report.TaskDescription, report.CustomerObservations, report.TechnicalObservations,
                   report.InstallationTypesJson, report.WorkKind, report.CustomWorkKind, report.Remarks,
                   report.ClosureFlagsJson, report.PayloadJson, report.IsSoftDeleted,
                   report.CreatedAt, report.UpdatedAt, report.SubmittedAt, report.DeletionScheduledAt
            from dbo.JobReports report
            where report.Id = @Id and report.OrganizationId = @OrganizationId;
            """,
            new { Id = id, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        CustomerRow? customer = null;
        if (row.CustomerId.HasValue)
        {
            customer = await connection.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
                "select * from dbo.Customers where Id = @Id and OrganizationId = @OrganizationId;",
                new { Id = row.CustomerId.Value, OrganizationId = organizationId },
                cancellationToken: cancellationToken));
        }

        var subcategories = await connection.QueryAsync<JobControlSubcategoryRow>(new CommandDefinition(
            "select * from dbo.JobControlSubcategoryDecisions where ReportId = @Id and OrganizationId = @OrganizationId order by InstallationTypeId, SubcategoryId;",
            new { Id = id, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        var checks = await connection.QueryAsync<JobControlCheckRow>(new CommandDefinition(
            "select * from dbo.JobControlChecks where ReportId = @Id and OrganizationId = @OrganizationId order by InstallationTypeId, SubcategoryId, ItemId;",
            new { Id = id, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        var links = await LoadLinksAsync(connection, organizationId, id, cancellationToken);
        var assignedUsers = await LoadAssignedUsersAsync(connection, organizationId, id, cancellationToken);
        var totalHours = await GetTotalHoursByJobAsync(connection, [id], cancellationToken);
        var worksheetEntries = await GetWorksheetEntriesByJobAsync(connection, id, cancellationToken);

        return ToResponse(row, customer, subcategories, checks, links, assignedUsers, worksheetEntries, totalHours.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<JobEventResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.events", token => GetEventsAsyncCoreAsync(id, organizationId, limit, offset, token), cancellationToken);

    private async Task<IReadOnlyList<JobEventResponse>?> GetEventsAsyncCoreAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from dbo.JobReports where Id = @Id and OrganizationId = @OrganizationId;",
            new { Id = id, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        if (exists == 0)
        {
            return null;
        }

        var rows = await connection.QueryAsync<JobEventRow>(new CommandDefinition(
            """
            select *
            from dbo.JobEvents
            where ReportId = @Id
              and OrganizationId = @OrganizationId
            order by CreatedAt desc
            offset @Offset rows fetch next @Limit rows only;
            """,
            new { Id = id, OrganizationId = organizationId, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken));

        return rows.Select(ToEventResponse).ToArray();
    }

    public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.update", token => UpdateAsyncCoreAsync(id, organizationId, request, token), cancellationToken);

    private async Task<JobReportResponse?> UpdateAsyncCoreAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id = @Id and OrganizationId = @OrganizationId;",
            new { Id = id, OrganizationId = organizationId },
            transaction,
            cancellationToken: cancellationToken));

        if (existing is null || !JobStatusPolicy.CanEdit(ParseStatus(existing.Status)))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        Guid? customerId = null;
        if (request.Customer?.Email is not null)
        {
            customerId = await customerRepository.UpsertByEmailAsync(organizationId, request.Customer, connection, transaction, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.JobReports
            set CustomerId = @CustomerId,
                ReportNumber = coalesce(@ReportNumber, ReportNumber),
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
            where Id = @Id and OrganizationId = @OrganizationId;
            """,
            new
            {
                Id = id,
                OrganizationId = organizationId,
                CustomerId = customerId,
                request.ReportNumber,
                ReportDate = ToDateTime(request.ReportDate),
                request.TaskDescription,
                request.CustomerObservations,
                request.TechnicalObservations,
                InstallationTypesJson = request.InstallationTypes is null ? null : ToJson(request.InstallationTypes),
                WorkKind = NormalizeOptional(request.WorkKind),
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
                "delete from dbo.JobControlSubcategoryDecisions where ReportId = @Id and OrganizationId = @OrganizationId;",
                new { Id = id, OrganizationId = organizationId },
                transaction,
                cancellationToken: cancellationToken));
            await ReplaceControlInstallationTypesAsync(connection, transaction, organizationId, id, request.ControlInstallationTypes, now, cancellationToken);
        }

        await InsertEventAsync(connection, transaction, organizationId, id, null, "updated", ToJsonNode(existing), ToJsonNode(request), now, cancellationToken);
        transaction.Commit();

        return await GetAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("jobs.transition", token => TransitionAsyncCoreAsync(id, organizationId, nextStatus, actorId, token), cancellationToken);

    private async Task<JobReportResponse?> TransitionAsyncCoreAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id = @Id and OrganizationId = @OrganizationId;",
            new { Id = id, OrganizationId = organizationId },
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
            where Id = @Id and OrganizationId = @OrganizationId;
            """,
            new { Id = id, OrganizationId = organizationId, Status = nextStatus.ToString(), UpdatedAt = now },
            transaction,
            cancellationToken: cancellationToken));

        await InsertEventAsync(connection, transaction, organizationId, id, actorId, nextStatus.ToString().ToLowerInvariant(), ToJsonNode(existing), ToJsonNode(new { status = nextStatus.ToString() }), now, cancellationToken);
        transaction.Commit();

        return await GetAsync(id, organizationId, cancellationToken);
    }

     public Task<JobReportResponse?> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
         retryPolicy.ExecuteAsync("jobs.delete", token => DeleteAsyncCoreAsync(id, organizationId, token), cancellationToken);

     private async Task<JobReportResponse?> DeleteAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
     {
         using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
         using var transaction = connection.BeginTransaction();

         var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
             "select * from dbo.JobReports where Id = @Id and OrganizationId = @OrganizationId;",
             new { Id = id, OrganizationId = organizationId },
             transaction,
             cancellationToken: cancellationToken));

         if (existing is null)
         {
             return null;
         }

         if (existing.IsSoftDeleted || existing.DeletionScheduledAt.HasValue)
         {
             transaction.Commit();
             return await GetAsync(id, organizationId, cancellationToken);
         }

         var now = DateTimeOffset.UtcNow;
         var deletionScheduledAt = now.Add(DeletionRetentionPeriod);

         await connection.ExecuteAsync(new CommandDefinition(
             "update dbo.JobReports set IsSoftDeleted = 1, DeletionScheduledAt = @DeletionScheduledAt, UpdatedAt = @UpdatedAt where Id = @Id and OrganizationId = @OrganizationId;",
             new { Id = id, OrganizationId = organizationId, DeletionScheduledAt = deletionScheduledAt, UpdatedAt = now },
             transaction,
             cancellationToken: cancellationToken));

         await InsertEventAsync(
             connection,
             transaction,
             organizationId,
             id,
             null,
             "deletionScheduled",
             ToJsonNode(existing),
             ToJsonNode(new { deletionScheduledAt }),
             now,
             cancellationToken);

         transaction.Commit();
         return await GetAsync(id, organizationId, cancellationToken);
     }

     public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
         retryPolicy.ExecuteAsync("jobs.restore-deletion", token => RestoreDeletionAsyncCoreAsync(id, organizationId, token), cancellationToken);

     private async Task<JobReportResponse?> RestoreDeletionAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
     {
         using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
         using var transaction = connection.BeginTransaction();

         var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
             "select * from dbo.JobReports where Id = @Id and OrganizationId = @OrganizationId;",
             new { Id = id, OrganizationId = organizationId },
             transaction,
             cancellationToken: cancellationToken));

         if (existing is null)
         {
             return null;
         }

         if (!existing.IsSoftDeleted && !existing.DeletionScheduledAt.HasValue)
         {
             transaction.Commit();
             return await GetAsync(id, organizationId, cancellationToken);
         }

         var now = DateTimeOffset.UtcNow;
         await connection.ExecuteAsync(new CommandDefinition(
             "update dbo.JobReports set IsSoftDeleted = 0, DeletionScheduledAt = null, UpdatedAt = @UpdatedAt where Id = @Id and OrganizationId = @OrganizationId;",
             new { Id = id, OrganizationId = organizationId, UpdatedAt = now },
             transaction,
             cancellationToken: cancellationToken));

         await InsertEventAsync(
             connection,
             transaction,
             organizationId,
             id,
             null,
             "deletionRestored",
             ToJsonNode(existing),
             ToJsonNode(new { deletionScheduledAt = (DateTimeOffset?)null }),
             now,
             cancellationToken);

         transaction.Commit();
         return await GetAsync(id, organizationId, cancellationToken);
     }

     public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
         retryPolicy.ExecuteAsync("jobs.purge-scheduled-deletions", token => PurgeDeletionScheduledBeforeAsyncCoreAsync(cutoff, token), cancellationToken);

     private async Task<int> PurgeDeletionScheduledBeforeAsyncCoreAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
     {
         using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
         using var transaction = connection.BeginTransaction();

         var dueJobIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
             "select Id from dbo.JobReports where DeletionScheduledAt is not null and DeletionScheduledAt <= @Cutoff;",
             new { Cutoff = cutoff },
             transaction,
             cancellationToken: cancellationToken))).ToArray();

         if (dueJobIds.Length == 0)
         {
             transaction.Commit();
             return 0;
         }

         await connection.ExecuteAsync(new CommandDefinition(
             "delete from dbo.JobReportLinks where SourceReportId in @Ids or TargetReportId in @Ids;",
             new { Ids = dueJobIds },
             transaction,
             cancellationToken: cancellationToken));

         var deletedCount = await connection.ExecuteAsync(new CommandDefinition(
             "delete from dbo.JobReports where Id in @Ids and DeletionScheduledAt is not null and DeletionScheduledAt <= @Cutoff;",
             new { Ids = dueJobIds, Cutoff = cutoff },
             transaction,
             cancellationToken: cancellationToken));

         transaction.Commit();
         return deletedCount;
     }

     public Task<JobReportResponse?> AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken) =>
         retryPolicy.ExecuteAsync("jobs.assign", token => AssignAsyncCoreAsync(jobId, organizationId, userIds, actorId, token), cancellationToken);

     private async Task<JobReportResponse?> AssignAsyncCoreAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken)
     {
         using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
         using var transaction = connection.BeginTransaction();

         var existing = await connection.QuerySingleOrDefaultAsync<JobReportRow>(new CommandDefinition(
             """
             select report.*
             from dbo.JobReports report
             where report.Id = @Id and report.OrganizationId = @OrganizationId;
             """,
             new { Id = jobId, OrganizationId = organizationId },
             transaction,
             cancellationToken: cancellationToken));

         if (existing is null)
         {
             return null;
         }

         var targetUserIds = NormalizeUserIds(userIds);
         var targetAssignedUsers = await LoadAssignedUsersByIdsAsync(connection, transaction, organizationId, targetUserIds, cancellationToken);
         if (targetAssignedUsers.Count != targetUserIds.Length)
         {
             return null;
         }

         var existingAssignedUsers = await LoadAssignedUsersAsync(connection, transaction, organizationId, jobId, cancellationToken);
         var existingUserIds = existingAssignedUsers.Select(user => user.Id).OrderBy(id => id).ToArray();
         var orderedTargetUserIds = targetUserIds.OrderBy(id => id).ToArray();
         if (existingUserIds.SequenceEqual(orderedTargetUserIds))
         {
             return await GetAsync(jobId, organizationId, cancellationToken);
         }

         var now = DateTimeOffset.UtcNow;
         await connection.ExecuteAsync(new CommandDefinition(
             "delete from dbo.JobAssignments where ReportId = @Id and OrganizationId = @OrganizationId;",
             new { Id = jobId, OrganizationId = organizationId },
             transaction,
             cancellationToken: cancellationToken));

         await ReplaceAssignedUsersAsync(connection, transaction, organizationId, jobId, targetUserIds, actorId, now, cancellationToken);

         await connection.ExecuteAsync(new CommandDefinition(
             "update dbo.JobReports set UpdatedAt = @UpdatedAt where Id = @Id and OrganizationId = @OrganizationId;",
             new { Id = jobId, OrganizationId = organizationId, UpdatedAt = now },
             transaction,
             cancellationToken: cancellationToken));

         var eventType = existingAssignedUsers.Count == 0 ? "assigned" : "reassigned";
         var before = ToJsonNode(new { assignedUsers = existingAssignedUsers });
         var after = ToJsonNode(new { assignedUsers = targetAssignedUsers });

         await InsertEventAsync(connection, transaction, organizationId, jobId, actorId, eventType, before, after, now, cancellationToken);

         transaction.Commit();
         return await GetAsync(jobId, organizationId, cancellationToken);
     }

    private static async Task ReplaceControlInstallationTypesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid reportId,
        IReadOnlyList<ControlInstallationTypeRequest>? installationTypes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (installationTypes is null)
        {
            return;
        }

        foreach (var installationType in installationTypes)
        {
            foreach (var subcategory in installationType.Subcategories)
            {
                var subcategoryDecisionId = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into dbo.JobControlSubcategoryDecisions (Id, OrganizationId, ReportId, InstallationTypeId, SubcategoryId, CreatedAt, UpdatedAt)
                    values (@Id, @OrganizationId, @ReportId, @InstallationTypeId, @SubcategoryId, @CreatedAt, @UpdatedAt);
                    """,
                    new
                    {
                        Id = subcategoryDecisionId,
                        OrganizationId = organizationId,
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
                        insert into dbo.JobControlChecks (Id, OrganizationId, ReportId, SubcategoryDecisionId, InstallationTypeId, SubcategoryId, ItemId, Checked, Note, CreatedAt, UpdatedAt)
                        values (@Id, @OrganizationId, @ReportId, @SubcategoryDecisionId, @InstallationTypeId, @SubcategoryId, @ItemId, @Checked, @Note, @CreatedAt, @UpdatedAt);
                        """,
                        new
                        {
                            Id = Guid.NewGuid(),
                            OrganizationId = organizationId,
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
        Guid organizationId,
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
            insert into dbo.JobEvents (Id, OrganizationId, ReportId, ActorId, EventType, BeforeJson, AfterJson, CreatedAt)
            values (@Id, @OrganizationId, @ReportId, @ActorId, @EventType, @BeforeJson, @AfterJson, @CreatedAt);
            """,
            new
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
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
        Guid organizationId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var links = await connection.QueryAsync<JobReportLinkRow>(new CommandDefinition(
            "select * from dbo.JobReportLinks where OrganizationId = @OrganizationId and (SourceReportId = @Id or TargetReportId = @Id);",
            new { Id = reportId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        var linkedIds = links.Select(link =>
            link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId).Distinct().ToArray();

        if (linkedIds.Length == 0)
            return [];

        var linkedReports = (await connection.QueryAsync<LinkedReportProjection>(new CommandDefinition(
            """
            select report.Id, report.ReportNumber, report.Status, c.Name as CustomerName
            from dbo.JobReports report
            left join dbo.Customers c on c.Id = report.CustomerId
            where report.OrganizationId = @OrganizationId and report.Id in @Ids;
            """,
            new { Ids = linkedIds, OrganizationId = organizationId },
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
        CustomerRow? customer,
        IEnumerable<JobControlSubcategoryRow> subcategories,
        IEnumerable<JobControlCheckRow> checks,
        IReadOnlyList<JobLinkInfoResponse> links,
        IReadOnlyList<AssignedUserResponse> assignedUsers,
        IReadOnlyList<WorksheetUserGroupResponse> worksheetEntries,
        decimal? totalHours = null)
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
             customer is not null ? new CustomerInfo(customer.Id, customer.Name, customer.Address, customer.Email, customer.ContactPerson, customer.Phone) : null,
             row.ReportNumber,
             ParseStatus(row.Status),
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
              assignedUsers,
             worksheetEntries,
             row.IsSoftDeleted,
             row.DeletionScheduledAt,
             totalHours);
    }

    private static Guid[] NormalizeUserIds(IEnumerable<Guid> userIds) =>
        userIds.Where(id => id != Guid.Empty).Distinct().ToArray();

    private static async Task ReplaceAssignedUsersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid reportId,
        IReadOnlyList<Guid> userIds,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var userId in userIds)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into dbo.JobAssignments (Id, OrganizationId, ReportId, UserId, AssignedByUserId, AssignedAt)
                values (@Id, @OrganizationId, @ReportId, @UserId, @AssignedByUserId, @AssignedAt);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ReportId = reportId,
                    UserId = userId,
                    AssignedByUserId = actorId,
                    AssignedAt = now
                },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static async Task<IReadOnlyList<AssignedUserResponse>> LoadAssignedUsersAsync(
        IDbConnection connection,
        Guid organizationId,
        Guid reportId,
        CancellationToken cancellationToken) =>
        (await LoadAssignedUsersAsync(connection, organizationId, [reportId], cancellationToken)).GetValueOrDefault(reportId) ?? [];

    private static async Task<IReadOnlyList<AssignedUserResponse>> LoadAssignedUsersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid reportId,
        CancellationToken cancellationToken) =>
        (await LoadAssignedUsersAsync(connection, organizationId, [reportId], cancellationToken, transaction)).GetValueOrDefault(reportId) ?? [];

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> LoadAssignedUsersAsync(
        IDbConnection connection,
        Guid organizationId,
        IEnumerable<Guid> reportIds,
        CancellationToken cancellationToken,
        IDbTransaction? transaction = null)
    {
        var normalizedReportIds = reportIds.Distinct().ToArray();
        if (normalizedReportIds.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<AssignedUserResponse>>();
        }

        var rows = await connection.QueryAsync<JobAssignmentUserProjection>(new CommandDefinition(
            """
            select assignment.ReportId, userRow.Id, userRow.DisplayName
            from dbo.JobAssignments assignment
            inner join dbo.Users userRow
              on userRow.OrganizationId = assignment.OrganizationId
             and userRow.Id = assignment.UserId
            where assignment.OrganizationId = @OrganizationId
              and assignment.ReportId in @ReportIds
            order by assignment.AssignedAt, userRow.DisplayName;
            """,
            new { OrganizationId = organizationId, ReportIds = normalizedReportIds },
            transaction,
            cancellationToken: cancellationToken));

        return rows
            .GroupBy(row => row.ReportId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new AssignedUserResponse(row.Id, row.DisplayName)).ToArray() as IReadOnlyList<AssignedUserResponse>);
    }

    private static async Task<IReadOnlyList<AssignedUserResponse>> LoadAssignedUsersByIdsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var rows = (await connection.QueryAsync<UserAssignmentProjection>(new CommandDefinition(
            "select Id, DisplayName from dbo.Users where OrganizationId = @OrganizationId and Id in @UserIds;",
            new { OrganizationId = organizationId, UserIds = userIds },
            transaction,
            cancellationToken: cancellationToken))).ToDictionary(row => row.Id);

        return userIds
            .Where(rows.ContainsKey)
            .Select(userId => new AssignedUserResponse(userId, rows[userId].DisplayName))
            .ToArray();
    }

    private sealed class LinkedReportProjection
    {
        public Guid Id { get; init; }
        public string? ReportNumber { get; init; }
        public string? Status { get; init; }
        public string? CustomerName { get; init; }
    }

    private sealed class JobAssignmentUserProjection
    {
        public Guid ReportId { get; init; }
        public Guid Id { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    private sealed class UserAssignmentProjection
    {
        public Guid Id { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    private sealed class WorksheetEntryProjection
    {
        public DateTime WorkDate { get; init; }
        public decimal HoursWorked { get; init; }
        public string DisplayName { get; init; } = "";
    }

    private static async Task<IReadOnlyList<WorksheetUserGroupResponse>> GetWorksheetEntriesByJobAsync(
        IDbConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<WorksheetEntryProjection>(new CommandDefinition(
            """
            select w.WorkDate, w.HoursWorked, u.DisplayName
            from dbo.Worksheets w
            inner join dbo.Users u on u.Id = w.UserId
            where w.JobId = @JobId
            order by u.DisplayName, w.WorkDate desc;
            """,
            new { JobId = jobId },
            cancellationToken: cancellationToken));

        return rows
            .GroupBy(r => r.DisplayName)
            .Select(g => new WorksheetUserGroupResponse(
                g.Key,
                g.Sum(r => r.HoursWorked),
                g.Select(r => new WorksheetDayEntry(
                    DateOnly.FromDateTime(r.WorkDate),
                    r.HoursWorked)).ToArray() as IReadOnlyList<WorksheetDayEntry>))
            .ToArray();
    }

    private sealed class JobTotalHoursProjection
    {
        public Guid JobId { get; init; }
        public decimal? TotalHours { get; init; }
    }

    private static async Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(
        IDbConnection connection,
        IEnumerable<Guid> jobIds,
        CancellationToken cancellationToken)
    {
        var ids = jobIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, decimal?>();
        }

        var rows = await connection.QueryAsync<JobTotalHoursProjection>(new CommandDefinition(
            """
            select JobId, cast(sum(HoursWorked) as decimal(5,2)) as TotalHours
            from dbo.Worksheets
            where JobId in @JobIds
            group by JobId;
            """,
            new { JobIds = ids },
            cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.JobId, r => r.TotalHours);
    }

    private static JobStatus ParseStatus(string status) => Enum.Parse<JobStatus>(status, ignoreCase: true);

    private static JobEventResponse ToEventResponse(JobEventRow row) => new(
        row.Id,
        row.ReportId,
        row.ActorId,
        row.EventType,
        ToJsonObject(row.BeforeJson),
        ToJsonObject(row.AfterJson),
        row.CreatedAt);

    private static JsonObject? ToJsonObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;

    private static DateTime? ToDateTime(DateOnly? value) =>
        value is null ? null : value.Value.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    private static string? ToSearchLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        return $"%{escaped}%";
    }

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private static IReadOnlyList<string> FromJsonList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
}
