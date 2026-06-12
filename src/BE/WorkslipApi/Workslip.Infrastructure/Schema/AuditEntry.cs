using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Workslip.Infrastructure.Schema;

internal sealed class AuditEntry(EntityEntry entry)
{
    public EntityEntry Entry { get; } = entry;
    public Guid OrganizationId { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? ReportId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public Dictionary<string, object?> KeyValues { get; } = new();
    public Dictionary<string, object?> BeforeValues { get; } = new();
    public Dictionary<string, object?> AfterValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();

    public AuditEntry Clone()
    {
        var clone = new AuditEntry(Entry)
        {
            OrganizationId = OrganizationId,
            ActorId = ActorId,
            ReportId = ReportId,
            EventType = EventType,
            Summary = Summary
        };
        foreach (var kv in KeyValues) clone.KeyValues[kv.Key] = kv.Value;
        foreach (var kv in BeforeValues) clone.BeforeValues[kv.Key] = kv.Value;
        foreach (var kv in AfterValues) clone.AfterValues[kv.Key] = kv.Value;
        clone.TemporaryProperties.AddRange(TemporaryProperties);
        return clone;
    }
}
