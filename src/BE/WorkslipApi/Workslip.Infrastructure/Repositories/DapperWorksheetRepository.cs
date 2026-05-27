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

    public async Task<WorksheetResponse> CreateAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("worksheets.create", async token =>
        {
            using var connection = await _connectionFactory.OpenConnectionAsync(token);
            using var transaction = connection.BeginTransaction();

            var now = DateTimeOffset.UtcNow;
            var worksheetId = Guid.NewGuid();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into dbo.Worksheets (
                    Id, OrganizationId, JobId, UserId, WorkDate, HoursWorked, SleptOnJob, CreatedAt, UpdatedAt
                )
                values (
                    @Id, @OrganizationId, @JobId, @UserId, @WorkDate, @HoursWorked, @SleptOnJob, @CreatedAt, @UpdatedAt
                );
                """,
                new
                {
                    Id = worksheetId,
                    // We need to get OrganizationId from either Job or User
                    // Let's get it from the JobReport (since JobId maps to JobReports.Id)
                    OrganizationId = (await connection.QuerySingleOrDefaultAsync<Guid?>(
                        "select OrganizationId from dbo.JobReports where Id = @JobId;",
                        new { JobId = request.JobId },
                        transaction)) ?? throw new InvalidOperationException($"Job with ID {request.JobId} not found"),
                    JobId = request.JobId,
                    UserId = request.UserId,
                    WorkDate = request.WorkDate,
                    HoursWorked = request.HoursWorked,
                    SleptOnJob = request.SleptOnJob,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                transaction));

            // Verify the user exists and belongs to the same organization
            var userOrgId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "select OrganizationId from dbo.Users where Id = @UserId;",
                new { UserId = request.UserId },
                transaction);

            if (!userOrgId.HasValue)
            {
                throw new InvalidOperationException($"User with ID {request.UserId} not found");
            }

            // Verify organization consistency
            var jobOrgId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "select OrganizationId from dbo.JobReports where Id = @JobId;",
                new { JobId = request.JobId },
                transaction);

            if (userOrgId != jobOrgId)
            {
                throw new InvalidOperationException("User and Job must belong to the same organization");
            }

            var worksheet = await connection.QuerySingleAsync<WorksheetRow>(new CommandDefinition(
                "select * from dbo.Worksheets where Id = @Id;",
                new { Id = worksheetId },
                transaction));

            transaction.Commit();

            return new WorksheetResponse(
                worksheet.Id,
                worksheet.OrganizationId,
                worksheet.JobId,
                worksheet.UserId,
                worksheet.WorkDate,
                worksheet.HoursWorked,
                worksheet.SleptOnJob,
                worksheet.CreatedAt,
                worksheet.UpdatedAt);
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
