using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Infrastructure.Repositories;

namespace Workslip.Infrastructure.Schema;

internal static class WorksheetRateSnapshots
{
    internal static async Task CaptureAsync(
        SqlDbContext db,
        IReadOnlyList<(Guid JobId, Guid OrganizationId)> jobs,
        CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            var worksheets = await db.Worksheets
                .Where(row => row.OrganizationId == job.OrganizationId && row.JobId == job.JobId)
                .ToListAsync(cancellationToken);
            var userRepository = new EfUserRepository(db, new SnapshotTenantContext(job.OrganizationId));
            var rates = new Dictionary<Guid, decimal?>();

            foreach (var userId in worksheets.Select(row => row.UserId).Distinct())
                rates[userId] = (await userRepository.GetByIdAsync(userId, cancellationToken))?.BillableHourlyRate;

            foreach (var worksheet in worksheets)
            {
                worksheet.BillableHourlyRateSnapshot = rates.GetValueOrDefault(worksheet.UserId);
                db.Entry(worksheet).Property(row => row.BillableHourlyRateSnapshot).IsModified = true;
            }
        }
    }

    private sealed class SnapshotTenantContext(Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => null;
    }
}
