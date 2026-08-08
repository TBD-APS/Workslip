using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

internal sealed class AuditChangeCollector
{
    private static readonly HashSet<string> AuditNoiseProperties = new(StringComparer.Ordinal)
    {
        "CreatedAt", "UpdatedAt", "AssignedAt", "OrganizationId", "SubmittedByUserId"
    };

    public AuditEntry BuildBaseEntry(AuditBuildContext context, EntityEntry entry)
    {
        var auditEntry = new AuditEntry(entry)
        {
            OrganizationId = ResolveOrganizationId(entry),
            ActorId = context.CurrentUser.UserId,
            EventType = entry.State.ToString().ToLowerInvariant(),
            ReportId = entry.Entity is IJobRelated jobRelated
                ? jobRelated.JobReportId
                : TryFindReportId(entry, context.DbContext)
        };

        CollectPropertyChanges(entry, auditEntry);
        return auditEntry;
    }

    public void CollectDeletedForeignKeyDisplays(AuditBuildContext context, EntityEntry entry, AuditEntry auditEntry)
    {
        foreach (var fk in entry.Metadata.GetForeignKeys())
        {
            if (fk.PrincipalEntityType.ClrType is not { } principalType)
                continue;

            var fkValues = fk.Properties
                .Select(p => (Name: p.Name, Value: entry.Property(p.Name).OriginalValue))
                .ToList();

            if (fkValues.Any(x => x.Value is null))
                continue;

            var pkValues = new Dictionary<string, object?>();
            for (var i = 0; i < fkValues.Count; i++)
            {
                var pkProp = fk.PrincipalKey.Properties[i];
                pkValues[pkProp.Name] = fkValues[i].Value;
            }

            var display = context.DisplayResolver.ResolveForeignKeyDisplayValue(principalType, pkValues, context.DbContext);
            if (display is null)
                continue;

            var key = string.Join("_", fkValues.Select(x => x.Name));
            auditEntry.BeforeValues[$"{key}_Display"] = display;
        }
    }

    public static void Finalize(AuditEntry auditEntry)
    {
        auditEntry.Summary = BuildSummary(auditEntry);
        RemoveInternalDisplayValues(auditEntry);
        ApplyTemporaryProperties(auditEntry);
    }

    public static bool ShouldSkip(AuditEntry auditEntry) =>
        auditEntry.EventType == AuditEventTypes.Modified
        && auditEntry.BeforeValues.Count == 0
        && auditEntry.AfterValues.Count == 0;

    private static void CollectPropertyChanges(EntityEntry entry, AuditEntry auditEntry)
    {
        foreach (var property in entry.Properties)
        {
            if (property.IsTemporary)
            {
                auditEntry.TemporaryProperties.Add(property);
                continue;
            }

            var propertyName = property.Metadata.Name;
            if (property.Metadata.IsPrimaryKey())
            {
                auditEntry.KeyValues[propertyName] = property.CurrentValue;
                continue;
            }

            if (AuditNoiseProperties.Contains(propertyName))
                continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    auditEntry.AfterValues[propertyName] = property.CurrentValue;
                    break;

                case EntityState.Deleted:
                    auditEntry.BeforeValues[propertyName] = property.OriginalValue;
                    break;

                case EntityState.Modified:
                    if (property.IsModified)
                    {
                        auditEntry.BeforeValues[propertyName] = property.OriginalValue;
                        auditEntry.AfterValues[propertyName] = property.CurrentValue;
                    }
                    break;
            }
        }
    }

    private static Guid? TryFindReportId(EntityEntry entry, DbContext dbContext)
    {
        if (entry.Entity is JobReportInstallationCategoryRow category)
        {
            var installation = dbContext.Set<JobReportInstallationRow>().Local.FirstOrDefault(x => x.Id == category.JobReportInstallationId)
                ?? dbContext.Set<JobReportInstallationRow>().AsNoTracking().FirstOrDefault(x => x.Id == category.JobReportInstallationId);
            return installation?.JobReportId;
        }

        if (entry.Entity is JobReportInstallationControlPointRow cp)
        {
            var cat = dbContext.Set<JobReportInstallationCategoryRow>().Local.FirstOrDefault(x => x.Id == cp.JobReportInstallationCategoryId)
                ?? dbContext.Set<JobReportInstallationCategoryRow>().AsNoTracking().Include(x => x.JobReportInstallation).FirstOrDefault(x => x.Id == cp.JobReportInstallationCategoryId);

            if (cat?.JobReportInstallation is not null) return cat.JobReportInstallation.JobReportId;
            if (cat is null) return null;

            var inst = dbContext.Set<JobReportInstallationRow>().Local.FirstOrDefault(x => x.Id == cat.JobReportInstallationId)
                ?? dbContext.Set<JobReportInstallationRow>().AsNoTracking().FirstOrDefault(x => x.Id == cat.JobReportInstallationId);

            return inst?.JobReportId;
        }

        return null;
    }

    private static Guid ResolveOrganizationId(EntityEntry entry)
    {
        var organizationProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "OrganizationId");
        return organizationProperty?.CurrentValue as Guid?
            ?? organizationProperty?.OriginalValue as Guid?
            ?? Guid.Empty;
    }

    private static void ApplyTemporaryProperties(AuditEntry auditEntry)
    {
        foreach (var prop in auditEntry.TemporaryProperties)
        {
            if (prop.Metadata.IsPrimaryKey())
            {
                auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
            }
            else
            {
                auditEntry.AfterValues[prop.Metadata.Name] = prop.CurrentValue;
            }
        }
    }

    private static void RemoveInternalDisplayValues(AuditEntry auditEntry)
    {
        foreach (var dict in new[] { auditEntry.BeforeValues, auditEntry.AfterValues })
        {
            var keys = dict.Keys.Where(k => k.EndsWith("_Display", StringComparison.Ordinal)).ToArray();
            foreach (var key in keys) dict.Remove(key);
        }
    }

    private static string BuildSummary(AuditEntry auditEntry)
    {
        if (!string.IsNullOrWhiteSpace(auditEntry.Summary))
            return auditEntry.Summary;

        var entityName = auditEntry.Entry.Entity.GetType().Name;
        if (entityName.EndsWith("Row", StringComparison.Ordinal))
            entityName = entityName[..^3];

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                return $"{entityName} oprettet";

            case AuditEventTypes.Deleted:
            {
                var display = auditEntry.BeforeValues
                    .Where(x => x.Key.EndsWith("_Display", StringComparison.Ordinal))
                    .Select(x => x.Value?.ToString())
                    .FirstOrDefault(v => v is not null);

                return display is not null
                    ? $"{entityName} '{display}' deleted"
                    : $"{entityName} deleted";
            }

            case AuditEventTypes.Modified:
            {
                var changed = auditEntry.BeforeValues.Keys
                    .Where(k => !k.EndsWith("_Display", StringComparison.Ordinal))
                    .ToList();

                if (changed.Count == 0)
                    return $"{entityName} opdateret";

                if (changed.Count == 1)
                {
                    var prop = changed[0];
                    var before = auditEntry.BeforeValues[prop]?.ToString() ?? "(tom)";
                    var after = auditEntry.AfterValues[prop]?.ToString() ?? "(tom)";
                    return $"{prop} ændret: '{before}' → '{after}'";
                }

                return $"{string.Join(", ", changed)} ændret";
            }

            default:
                return $"{entityName} {auditEntry.EventType}";
        }
    }
}