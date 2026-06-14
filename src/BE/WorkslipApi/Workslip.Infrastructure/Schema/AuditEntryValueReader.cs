using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Workslip.Infrastructure.Schema;

internal static class AuditEntryValueReader
{
    public static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}
