using Microsoft.EntityFrameworkCore;

namespace Workslip.Infrastructure.Schema;

internal static class WorksheetRateSnapshots
{
    internal static void Capture(SqlDbContext db, IReadOnlyList<(Guid JobId, Guid OrganizationId)> jobs)
    {
        foreach (var job in jobs)
        {
            var worksheets = db.Worksheets
                .Where(row => row.OrganizationId == job.OrganizationId && row.JobId == job.JobId)
                .ToList();
            var userIds = worksheets.Select(row => row.UserId).Distinct().ToArray();
            if (userIds.Length == 0) continue;

            var rates = db.Users.AsNoTracking()
                .Where(user => user.OrganizationId == job.OrganizationId && userIds.Contains(user.Id))
                .ToDictionary(user => user.Id, user => user.BillableHourlyRate);

            foreach (var worksheet in worksheets)
                worksheet.BillableHourlyRateSnapshot = rates.GetValueOrDefault(worksheet.UserId);
        }
    }

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
            var userIds = worksheets.Select(row => row.UserId).Distinct().ToArray();
            if (userIds.Length == 0) continue;

            var rates = await db.Users.AsNoTracking()
                .Where(user => user.OrganizationId == job.OrganizationId && userIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.BillableHourlyRate, cancellationToken);

            foreach (var worksheet in worksheets)
                worksheet.BillableHourlyRateSnapshot = rates.GetValueOrDefault(worksheet.UserId);
        }
    }
}
