namespace Workslip.Application.Worksheets;

public static class WorksheetHourRules
{
    public const decimal MaxDailyHours = 24m;
    public const string DailyLimitMessage = "En medarbejder må højst registrere 24 timer pr. dag.";

    public static bool IsValidIncrement(decimal hours) =>
        decimal.Remainder(hours * 4m, 1m) == 0m;
}

public sealed class WorksheetDailyHoursExceededException()
    : InvalidOperationException(WorksheetHourRules.DailyLimitMessage);
