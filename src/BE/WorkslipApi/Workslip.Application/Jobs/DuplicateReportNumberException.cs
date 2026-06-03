namespace Workslip.Application.Jobs;

public sealed class DuplicateReportNumberException : Exception
{
    public DuplicateReportNumberException(string? reportNumber, Exception? innerException = null)
        : base("Report number already exists in the organization.", innerException)
    {
        ReportNumber = reportNumber;
    }

    public string? ReportNumber { get; }
}
