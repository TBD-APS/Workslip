using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Application.Worksheets;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class WorksheetDailyHoursInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is SqlDbContext context)
        {
            ValidateAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SqlDbContext context)
        {
            await ValidateAsync(context, cancellationToken);
        }

        return result;
    }

    private static async Task ValidateAsync(SqlDbContext context, CancellationToken cancellationToken)
    {
        var entries = context.ChangeTracker
            .Entries<WorksheetRow>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (entries.Length == 0)
            return;

        var keys = new HashSet<DailyHoursKey>();
        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                keys.Add(CurrentKey(entry));

            if (entry.State is EntityState.Modified or EntityState.Deleted)
                keys.Add(OriginalKey(entry));
        }

        var trackedIds = entries
            .Select(entry => entry.Entity.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        foreach (var key in keys.OrderBy(key => key.OrganizationId).ThenBy(key => key.UserId).ThenBy(key => key.WorkDate))
        {
            await AcquireDailyLockAsync(context, key, cancellationToken);

            var persistedQuery = context.Worksheets
                .AsNoTracking()
                .Where(row => row.OrganizationId == key.OrganizationId
                    && row.UserId == key.UserId
                    && row.WorkDate == key.WorkDate);

            if (trackedIds.Length > 0)
                persistedQuery = persistedQuery.Where(row => !trackedIds.Contains(row.Id));

            var persistedHours = await persistedQuery.SumAsync(row => row.HoursWorked, cancellationToken);
            var pendingHours = entries
                .Where(entry => entry.State != EntityState.Deleted && CurrentKey(entry) == key)
                .Sum(entry => entry.Entity.HoursWorked);

            if (persistedHours + pendingHours > WorksheetHourRules.MaxDailyHours)
                throw new WorksheetDailyHoursExceededException();
        }
    }

    private static async Task AcquireDailyLockAsync(
        SqlDbContext context,
        DailyHoursKey key,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer() || context.Database.CurrentTransaction is null)
            return;

        var resource = $"worksheet-hours:{key.OrganizationId:N}:{key.UserId:N}:{key.WorkDate:yyyyMMdd}";
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            IF @result < 0
                THROW 51000, 'Could not acquire worksheet daily-hours lock.', 1;
            """, cancellationToken);
    }

    private static DailyHoursKey CurrentKey(EntityEntry<WorksheetRow> entry) =>
        new(entry.Entity.OrganizationId, entry.Entity.UserId, entry.Entity.WorkDate.Date);

    private static DailyHoursKey OriginalKey(EntityEntry<WorksheetRow> entry) =>
        new(
            entry.OriginalValues.GetValue<Guid>(nameof(WorksheetRow.OrganizationId)),
            entry.OriginalValues.GetValue<Guid>(nameof(WorksheetRow.UserId)),
            entry.OriginalValues.GetValue<DateTime>(nameof(WorksheetRow.WorkDate)).Date);

    private readonly record struct DailyHoursKey(Guid OrganizationId, Guid UserId, DateTime WorkDate);
}
