using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

internal sealed class AuditDisplayResolver
{
    public string ResolveReportDisplayValue(Guid organizationId, Guid reportId, DbContext dbContext) =>
        dbContext.Set<JobReportRow>().Local
            .Where(x => x.OrganizationId == organizationId && x.Id == reportId)
            .Select(x => x.ReportNumber)
            .FirstOrDefault()
        ?? dbContext.Set<JobReportRow>().AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Id == reportId)
            .Select(x => x.ReportNumber)
            .FirstOrDefault()
        ?? "Ukendt sag";

    public string ResolveUserDisplayValue(Guid organizationId, Guid userId, DbContext dbContext) =>
        dbContext.Set<UserDataRow>().Local
            .Where(x => x.OrganizationId == organizationId && x.Id == userId)
            .Select(x => x.DisplayName)
            .FirstOrDefault()
        ?? dbContext.Set<UserDataRow>().AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Id == userId)
            .Select(x => x.DisplayName)
            .FirstOrDefault()
        ?? "Ukendt bruger";

    public string? ResolveCustomerDisplayValue(Guid organizationId, Guid? customerId, DbContext dbContext)
    {
        if (customerId is null)
            return null;

        return dbContext.Set<CustomerRow>().Local
            .Where(x => x.OrganizationId == organizationId && x.Id == customerId.Value)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? dbContext.Set<CustomerRow>().AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Id == customerId.Value)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? "Ukendt kunde";
    }



    public string? ResolveWorkKindDisplayValue(Guid? workKindId, DbContext dbContext)
    {
        if (workKindId is null)
            return null;

        return dbContext.Set<JobWorkKindRow>().Local
            .Where(x => x.Id == workKindId.Value)
            .Select(x => x.Label)
            .FirstOrDefault()
        ?? dbContext.Set<JobWorkKindRow>().AsNoTracking()
            .Where(x => x.Id == workKindId.Value)
            .Select(x => x.Label)
            .FirstOrDefault()
        ?? "Ukendt opgavetype";
    }



    public string ResolveInstallationTypeDisplayValue(Guid organizationId, Guid installationTypeDefinitionId, DbContext dbContext) =>
        dbContext.Set<InstallationTypeDefinitionRow>().Local
            .Where(x => x.OrganizationId == organizationId && x.Id == installationTypeDefinitionId)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? dbContext.Set<InstallationTypeDefinitionRow>().AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Id == installationTypeDefinitionId)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? "Ukendt anlægstype";

    public string ResolveControlCategoryDisplayValue(Guid categoryId, DbContext dbContext) =>
        dbContext.Set<ControlCategoryRow>().Local
            .Where(x => x.Id == categoryId)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? dbContext.Set<ControlCategoryRow>().AsNoTracking()
            .Where(x => x.Id == categoryId)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? "Ukendt kategori";

    public string ResolveControlPointDisplayValue(Guid controlPointId, DbContext dbContext) =>
        dbContext.Set<ControlPointRow>().Local
            .Where(x => x.Id == controlPointId)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? dbContext.Set<ControlPointRow>().AsNoTracking()
            .Where(x => x.Id == controlPointId)
            .Select(x => x.Name)
            .FirstOrDefault()
        ?? "Ukendt kontrolpunkt";



    public string ResolveClosureFlagDisplayValue(Guid closureFlagId, DbContext dbContext) =>
        dbContext.Set<JobClosureFlagRow>().Local
            .Where(x => x.Id == closureFlagId)
            .Select(x => x.Label)
            .FirstOrDefault()
        ?? dbContext.Set<JobClosureFlagRow>().AsNoTracking()
            .Where(x => x.Id == closureFlagId)
            .Select(x => x.Label)
            .FirstOrDefault()
        ?? "Ukendt afslutningsflag";

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
