using System.Data;
using Dapper;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperWorksheetRepository : IWorksheetRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public DapperWorksheetRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy)
    {
        _connectionFactory = connectionFactory;
        _retryPolicy = retryPolicy;
    }

    public async Task<WorksheetResponse> UpsertAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("worksheets.upsert", async token =>
        {
            using var connection = await _connectionFactory.OpenConnectionAsync(token);
            using var transaction = connection.BeginTransaction();

            var now = DateTimeOffset.UtcNow;

            // Verify the user exists and belongs to the same organization as the job
            var userOrgId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "select OrganizationId from dbo.Users where Id = @UserId;",
                new { UserId = request.UserId },
                transaction);

            if (!userOrgId.HasValue)
            {
                throw new InvalidOperationException($"User with ID {request.UserId} not found");
            }

            var jobOrgId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "select OrganizationId from dbo.JobReports where Id = @JobId;",
                new { JobId = request.JobId },
                transaction);

            if (!jobOrgId.HasValue)
            {
                throw new InvalidOperationException($"Job with ID {request.JobId} not found");
            }

            if (userOrgId != jobOrgId)
            {
                throw new InvalidOperationException("User and Job must belong to the same organization");
            }

            // Upsert: update if exists for same (JobId, UserId, WorkDate), otherwise insert
            var worksheetId = Guid.NewGuid();
            var workDateParam = request.WorkDate.ToDateTime(TimeOnly.MinValue);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update dbo.Worksheets
                set HoursWorked = @HoursWorked, SleptOnJob = @SleptOnJob, UpdatedAt = @UpdatedAt
                where JobId = @JobId and UserId = @UserId and WorkDate = @WorkDate;

                if @@rowcount = 0
                    insert into dbo.Worksheets (Id, OrganizationId, JobId, UserId, WorkDate, HoursWorked, SleptOnJob, CreatedAt, UpdatedAt)
                    values (@Id, @OrganizationId, @JobId, @UserId, @WorkDate, @HoursWorked, @SleptOnJob, @CreatedAt, @UpdatedAt);
                """,
                new
                {
                    Id = worksheetId,
                    OrganizationId = jobOrgId.Value,
                    JobId = request.JobId,
                    UserId = request.UserId,
                    WorkDate = workDateParam,
                    HoursWorked = request.HoursWorked,
                    SleptOnJob = request.SleptOnJob,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                transaction));

            var worksheet = await connection.QuerySingleAsync<WorksheetRow>(new CommandDefinition(
                "select * from dbo.Worksheets where JobId = @JobId and UserId = @UserId and WorkDate = @WorkDate;",
                new { JobId = request.JobId, UserId = request.UserId, WorkDate = workDateParam },
                transaction));

            transaction.Commit();

            return new WorksheetResponse(
                worksheet.Id,
                worksheet.OrganizationId,
                worksheet.JobId,
                worksheet.UserId,
                DateOnly.FromDateTime(worksheet.WorkDate),
                worksheet.HoursWorked,
                worksheet.SleptOnJob,
                worksheet.CreatedAt,
                worksheet.UpdatedAt);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("worksheets.list-by-job", async token =>
        {
            using var connection = await _connectionFactory.OpenConnectionAsync(token);

            var rows = await connection.QueryAsync<WorksheetRow>(new CommandDefinition(
                """
                select *
                from dbo.Worksheets
                where JobId = @JobId
                order by WorkDate desc, CreatedAt desc;
                """,
                new { JobId = jobId },
                cancellationToken: token));

            return rows.Select(w => new WorksheetResponse(
                w.Id,
                w.OrganizationId,
                w.JobId,
                w.UserId,
                DateOnly.FromDateTime(w.WorkDate),
                w.HoursWorked,
                w.SleptOnJob,
                w.CreatedAt,
                w.UpdatedAt)).ToArray();
        }, cancellationToken);
    }

    public async Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("worksheets.delete", async token =>
        {
            using var connection = await _connectionFactory.OpenConnectionAsync(token);
            using var transaction = connection.BeginTransaction();

            // Verify the worksheet exists and belongs to the specified job
            var existingWorksheet = await connection.QuerySingleOrDefaultAsync<WorksheetRow>(new CommandDefinition(
                "select * from dbo.Worksheets where Id = @Id and JobId = @JobId;",
                new { Id = worksheetId, JobId = jobId },
                transaction));

            if (existingWorksheet == null)
            {
                // Worksheet not found - consider this a successful delete (idempotent)
                transaction.Commit();
                return;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "delete from dbo.Worksheets where Id = @Id and JobId = @JobId;",
                new { Id = worksheetId, JobId = jobId },
                transaction));

            transaction.Commit();
        }, cancellationToken);
    }
}
