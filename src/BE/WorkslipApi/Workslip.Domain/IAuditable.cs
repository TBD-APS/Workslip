namespace Workslip.Domain;

public interface IAuditable { }

public interface IJobRelated : IAuditable
{
    Guid JobReportId { get; }
}
