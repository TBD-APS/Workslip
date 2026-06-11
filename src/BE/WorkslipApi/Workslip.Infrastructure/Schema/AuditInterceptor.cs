using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class AuditInterceptor(ICurrentUserContext currentUser) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly AsyncLocal<bool> IsSaving = new();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsSaving.Value)
            return result;

        IsSaving.Value = true;
        try
        {
            var dbContext = eventData.Context;
            if (dbContext is null) return result;

            var auditEntries = OnBeforeSaveChanges(dbContext);

            var interceptionResult = await base.SavingChangesAsync(eventData, result, cancellationToken);

            if (auditEntries.Count > 0)
            {
                await OnAfterSaveChanges(dbContext, auditEntries, cancellationToken);
            }

            return interceptionResult;
        }
        finally
        {
            IsSaving.Value = false;
        }
    }

    private List<AuditEntry> OnBeforeSaveChanges(DbContext dbContext)
    {
        if (dbContext is SqlDbContext sqlContext && sqlContext.IsSeeding)
            return [];

        dbContext.ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.Entity is JobEventRow || entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            if (entry.Entity is not IAuditable)
                continue;

            var auditEntry = new AuditEntry(entry)
            {
                OrganizationId = currentUser.OrganizationId ?? Guid.Empty,
                ActorId = currentUser.UserId,
                EventType = entry.State.ToString().ToLowerInvariant()
            };

            // Link to job if possible
            if (entry.Entity is IJobRelated jobRelated)
            {
                auditEntry.ReportId = jobRelated.JobReportId;
            }
            else
            {
                auditEntry.ReportId = TryFindReportId(entry, dbContext);
            }

            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary)
                {
                    auditEntry.TemporaryProperties.Add(property);
                    continue;
                }

                string propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[propertyName] = property.CurrentValue;
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.AfterValues[propertyName] = property.CurrentValue;
                        break;

                    case EntityState.Deleted:
                        auditEntry.BeforeValues[propertyName] = property.OriginalValue;
                        break;

                    case EntityState.Modified:
                        if (property.IsModified && propertyName != "UpdatedAt")
                        {
                            auditEntry.BeforeValues[propertyName] = property.OriginalValue;
                            auditEntry.AfterValues[propertyName] = property.CurrentValue;
                        }
                        break;
                }
            }

            if (entry.State is EntityState.Modified && auditEntry.BeforeValues.Count == 0 && auditEntry.AfterValues.Count == 0)
                continue;

            auditEntries.Add(auditEntry);
        }

        return auditEntries;
    }

    private static Guid? TryFindReportId(EntityEntry entry, DbContext dbContext)
    {
        // Category -> Installation -> JobReport
        if (entry.Entity is JobReportInstallationCategoryRow category)
        {
            var installation = dbContext.Set<JobReportInstallationRow>().Local.FirstOrDefault(x => x.Id == category.JobReportInstallationId)
                ?? dbContext.Set<JobReportInstallationRow>().AsNoTracking().FirstOrDefault(x => x.Id == category.JobReportInstallationId);
            return installation?.JobReportId;
        }

        // ControlPoint -> Category -> Installation -> JobReport
        if (entry.Entity is JobReportInstallationControlPointRow cp)
        {
            var cat = dbContext.Set<JobReportInstallationCategoryRow>().Local.FirstOrDefault(x => x.Id == cp.JobReportInstallationCategoryId)
                ?? dbContext.Set<JobReportInstallationCategoryRow>().AsNoTracking().Include(x => x.JobReportInstallation).FirstOrDefault(x => x.Id == cp.JobReportInstallationCategoryId);
            
            if (cat?.JobReportInstallation != null) return cat.JobReportInstallation.JobReportId;
            
            // If JobReportInstallation was not included in the AsNoTracking query
            var inst = dbContext.Set<JobReportInstallationRow>().Local.FirstOrDefault(x => x.Id == cat.JobReportInstallationId)
                ?? dbContext.Set<JobReportInstallationRow>().AsNoTracking().FirstOrDefault(x => x.Id == cat.JobReportInstallationId);
            
            return inst?.JobReportId;
        }

        return null;
    }

    private async Task OnAfterSaveChanges(DbContext dbContext, List<AuditEntry> auditEntries, CancellationToken cancellationToken)
    {
        var rows = new List<JobEventRow>();
        foreach (var auditEntry in auditEntries)
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

            var eventRow = new JobEventRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = auditEntry.OrganizationId,
                ReportId = auditEntry.ReportId,
                ActorId = auditEntry.ActorId,
                EventType = auditEntry.EventType,
                BeforeJson = auditEntry.BeforeValues.Count > 0 ? JsonSerializer.Serialize(auditEntry.BeforeValues, JsonOptions) : null,
                AfterJson = auditEntry.AfterValues.Count > 0 ? JsonSerializer.Serialize(auditEntry.AfterValues, JsonOptions) : null,
                CreatedAt = DateTimeOffset.UtcNow
            };

            rows.Add(eventRow);
        }

        dbContext.Set<JobEventRow>().AddRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class AuditEntry(EntityEntry entry)
{
    public EntityEntry Entry { get; } = entry;
    public Guid OrganizationId { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? ReportId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Dictionary<string, object?> KeyValues { get; } = new();
    public Dictionary<string, object?> BeforeValues { get; } = new();
    public Dictionary<string, object?> AfterValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();
}
