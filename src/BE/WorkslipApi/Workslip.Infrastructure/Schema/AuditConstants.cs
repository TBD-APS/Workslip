namespace Workslip.Infrastructure.Schema;

internal static class AuditEventTypes
{
    public const string Added = "added";
    public const string Modified = "modified";
    public const string Deleted = "deleted";
}

internal static class AuditFields
{
    public const string AssignedUser = "AssignedUser";
    public const string LinkedReport = "LinkedReport";
    public const string WorkKind = "WorkKind";
    public const string InstallationType = "InstallationType";
    public const string InstallationCategory = "InstallationCategory";
    public const string ControlPoint = "ControlPoint";
    public const string ClosureFlag = "ClosureFlag";
    public const string Customer = "Customer";
    public const string Report = "Report";
}

internal static class AuditDisplayNames
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "CustomerId", "Kunde" },
        { "JobId", "Sag" },
        { "ReportId", "Sag" },
        { "ReportNumber", "Sagsnummer" },
        { "Status", "Status" },
        { "ReportDate", "Sagsdato" },
        { "TaskDescription", "Opgavebeskrivelse" },
        { "CustomerObservations", "Kundens observationer" },
        { "TechnicalObservations", "Tekniske observationer" },
        { "Remarks", "Bemærkninger" },
        { "CustomWorkKind", "Anden opgavetype" },
        { "WorkDate", "Arbejdsdato" },
        { "HoursWorked", "Timer" },
        { "SleptOnJob", "Overnatning" },
        { AuditFields.AssignedUser, "Tildelt bruger" },
        { AuditFields.LinkedReport, "Relateret sag" },
        { AuditFields.WorkKind, "Opgavetype" },
        { AuditFields.InstallationType, "Anlægstype" },
        { AuditFields.InstallationCategory, "Kategori" },
        { AuditFields.ControlPoint, "Kontrolpunkt" },
        { AuditFields.ClosureFlag, "Afslutningsflag" },
        { AuditFields.Customer, "Kunde" },
        { AuditFields.Report, "Sag" },
        { "IsChecked", "Afkrydset" },
        { "IsIrrelevant", "Ikke relevant" },
        { "IsSoftDeleted", "Slettet" },
        { "DeletionScheduledAt", "Sletning planlagt" }
    };
}
