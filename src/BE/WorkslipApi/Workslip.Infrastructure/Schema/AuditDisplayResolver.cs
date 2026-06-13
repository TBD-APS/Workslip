using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

internal sealed class AuditDisplayResolver
{
    public string ResolveReportDisplayValue(AuditBuildContext context, Guid organizationId, Guid reportId) =>
        GetOrAdd(context.ReportDisplayCache, (organizationId, reportId), () =>
            context.DbContext.Set<JobReportRow>().Local
                .Where(x => x.OrganizationId == organizationId && x.Id == reportId)
                .Select(x => x.ReportNumber)
                .FirstOrDefault()
            ?? context.DbContext.Set<JobReportRow>().AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == reportId)
                .Select(x => x.ReportNumber)
                .FirstOrDefault()
            ?? "Ukendt sag");

    public string ResolveUserDisplayValue(AuditBuildContext context, Guid organizationId, Guid userId) =>
        GetOrAdd(context.UserDisplayCache, (organizationId, userId), () =>
            context.DbContext.Set<UserDataRow>().Local
                .Where(x => x.OrganizationId == organizationId && x.Id == userId)
                .Select(x => x.DisplayName)
                .FirstOrDefault()
            ?? context.DbContext.Set<UserDataRow>().AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == userId)
                .Select(x => x.DisplayName)
                .FirstOrDefault()
            ?? "Ukendt bruger");

    public string? ResolveCustomerDisplayValue(AuditBuildContext context, Guid organizationId, Guid? customerId)
    {
        if (customerId is null)
            return null;

        return GetOrAdd(context.CustomerDisplayCache, (organizationId, customerId.Value), () =>
            context.DbContext.Set<CustomerRow>().Local
                .Where(x => x.OrganizationId == organizationId && x.Id == customerId.Value)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? context.DbContext.Set<CustomerRow>().AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == customerId.Value)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? "Ukendt kunde");
    }

    public string? ResolveWorkKindDisplayValue(AuditBuildContext context, Guid? workKindId)
    {
        if (workKindId is null)
            return null;

        return GetOrAdd(context.WorkKindDisplayCache, workKindId.Value, () =>
            context.DbContext.Set<JobWorkKindRow>().Local
                .Where(x => x.Id == workKindId.Value)
                .Select(x => x.Label)
                .FirstOrDefault()
            ?? context.DbContext.Set<JobWorkKindRow>().AsNoTracking()
                .Where(x => x.Id == workKindId.Value)
                .Select(x => x.Label)
                .FirstOrDefault()
            ?? "Ukendt opgavetype");
    }

    public string ResolveInstallationTypeDisplayValue(AuditBuildContext context, Guid organizationId, Guid installationTypeDefinitionId) =>
        GetOrAdd(context.InstallationTypeDisplayCache, (organizationId, installationTypeDefinitionId), () =>
            context.DbContext.Set<InstallationTypeDefinitionRow>().Local
                .Where(x => x.OrganizationId == organizationId && x.Id == installationTypeDefinitionId)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? context.DbContext.Set<InstallationTypeDefinitionRow>().AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == installationTypeDefinitionId)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? "Ukendt anlægstype");

    public string ResolveControlCategoryDisplayValue(AuditBuildContext context, Guid categoryId) =>
        GetOrAdd(context.ControlCategoryDisplayCache, categoryId, () =>
            context.DbContext.Set<ControlCategoryRow>().Local
                .Where(x => x.Id == categoryId)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? context.DbContext.Set<ControlCategoryRow>().AsNoTracking()
                .Where(x => x.Id == categoryId)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? "Ukendt kategori");

    public string ResolveControlPointDisplayValue(AuditBuildContext context, Guid controlPointId) =>
        GetOrAdd(context.ControlPointDisplayCache, controlPointId, () =>
            context.DbContext.Set<ControlPointRow>().Local
                .Where(x => x.Id == controlPointId)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? context.DbContext.Set<ControlPointRow>().AsNoTracking()
                .Where(x => x.Id == controlPointId)
                .Select(x => x.Name)
                .FirstOrDefault()
            ?? "Ukendt kontrolpunkt");

    public string ResolveClosureFlagDisplayValue(AuditBuildContext context, Guid closureFlagId) =>
        GetOrAdd(context.ClosureFlagDisplayCache, closureFlagId, () =>
            context.DbContext.Set<JobClosureFlagRow>().Local
                .Where(x => x.Id == closureFlagId)
                .Select(x => x.Label)
                .FirstOrDefault()
            ?? context.DbContext.Set<JobClosureFlagRow>().AsNoTracking()
                .Where(x => x.Id == closureFlagId)
                .Select(x => x.Label)
                .FirstOrDefault()
            ?? "Ukendt afslutningsflag");

    public string? ResolveForeignKeyDisplayValue(Type type, Dictionary<string, object?> pkValues, DbContext dbContext)
    {
        if (TryGetValue<Guid>(pkValues, "Id", out var id))
        {
            if (type == typeof(UserDataRow))
                return dbContext.Set<UserDataRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.DisplayName).FirstOrDefault();

            if (type == typeof(JobWorkKindRow))
                return dbContext.Set<JobWorkKindRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Label).FirstOrDefault();

            if (type == typeof(JobClosureFlagRow))
                return dbContext.Set<JobClosureFlagRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Label).FirstOrDefault();

            if (type == typeof(InstallationTypeDefinitionRow))
                return dbContext.Set<InstallationTypeDefinitionRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefault();

            if (type == typeof(ControlPointRow))
                return dbContext.Set<ControlPointRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefault();

            if (type == typeof(ControlCategoryRow))
                return dbContext.Set<ControlCategoryRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefault();

            if (type == typeof(InviteTokenRow))
                return dbContext.Set<InviteTokenRow>().AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.Email).FirstOrDefault();
        }

        if (TryGetValue<Guid>(pkValues, "OrganizationId", out var orgId)
            && TryGetValue<Guid>(pkValues, "Id", out var compId))
        {
            if (type == typeof(JobReportRow))
                return dbContext.Set<JobReportRow>().AsNoTracking()
                    .Where(x => x.OrganizationId == orgId && x.Id == compId)
                    .Select(x => x.ReportNumber).FirstOrDefault();

            if (type == typeof(CustomerRow))
                return dbContext.Set<CustomerRow>().AsNoTracking()
                    .Where(x => x.OrganizationId == orgId && x.Id == compId)
                    .Select(x => x.Name).FirstOrDefault();
        }

        return null;
    }

    private static TValue GetOrAdd<TKey, TValue>(IDictionary<TKey, TValue> cache, TKey key, Func<TValue> resolve)
        where TKey : notnull
    {
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var value = resolve();
        cache[key] = value;
        return value;
    }

    private static bool TryGetValue<T>(Dictionary<string, object?> dict, string key, out T value)
    {
        if (dict.TryGetValue(key, out var obj) && obj is T t)
        {
            value = t;
            return true;
        }

        value = default!;
        return false;
    }
}
