using Workslip.Application.Jobs;

namespace Workslip.Infrastructure.Mappers;

public static class WorksheetMapper
{
    public sealed record WorksheetEntryProjection
    {
        public DateTime WorkDate { get; init; }
        public decimal HoursWorked { get; init; }
        public string DisplayName { get; init; } = "";
    }

    public sealed record WorksheetMyProjection
    {
        public DateTime WorkDate { get; init; }
        public Guid JobId { get; init; }
        public string? ReportNumber { get; init; }
        public string CustomerName { get; init; } = "";
        public string? CustomerAddress { get; init; }
        public bool HasOutlay { get; init; }
        public decimal HoursWorked { get; init; }
        public string UserDisplayName { get; init; } = "";
        public string? JobType { get; init; }
    }

    public static IReadOnlyList<WorksheetUserGroupResponse> ToGroupedResponse(
        IReadOnlyList<WorksheetEntryProjection> rows)
    {
        return rows
            .GroupBy(r => r.DisplayName)
            .Select(g => new WorksheetUserGroupResponse(
                g.Key,
                g.Sum(r => r.HoursWorked),
                g.Select(r => new WorksheetDayEntry(DateOnly.FromDateTime(r.WorkDate), r.HoursWorked))
                    .ToArray()))
            .ToArray();
    }
}
