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
    public const string ControlPoints = "ControlPoints";
}

internal static class AuditDisplayNames
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "CustomerId", "Kunde" },
        { "CustomerName", "Kundenavn" },
        { "CustomerEmail", "Kunde e-mail" },
        { "CustomerPhone", "Kundes telefon" },
        { "CustomerAddress", "Kundeadresse" },
        { "DestinationAddress", "Adresse (destination)" },
        { "DestinationZipCode", "Destination postnummer" },
        { "DestinationCity", "Destination by" },
        { "JobId", "Sag" },
        { "ReportId", "Sag" },
        { "ReportNumber", "Sagsnummer" },
        { "Status", "Status" },
        { "ReportDate", "Sagsdato" },
        { "TaskDescription", "Opgavebeskrivelse" },
        { "CustomerObservations", "Oplysninger til kunden" },
        { "TechnicalObservations", "Kommentar til sagen" },
        { "Remarks", "Begrundelse for irrelevante kontrolpunkter" },
        { "CustomWorkKind", "Service/Andet" },
        { "WorkDate", "Arbejdsdato" },
        { "HoursWorked", "Timer" },
        { "SleptOnJob", "Overnatning" },
        { AuditFields.AssignedUser, "Tildelt medarbejder" },
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
        { "CustomerContactPerson", "Kundens kontaktperson" },
        { "SubmittedAt", "Attesteret" },
        { "IsSoftDeleted", "Slettet" },
        { "DeletionScheduledAt", "Sletning planlagt" }
    };
}

internal static class AuditDisplayValues
{
    public const string Checked = "✓";
    public const string Unchecked = "✗";
}

internal static class AuditSuffixes
{
    public const string Irrelevant = "(irrelevant)";
    public const string ControlPointSeparator = " / ";
    public const string Status = "Status";
}

internal static class AuditSummaryTemplates
{
    // Job assignment
    public const string AssignmentAdded = "{0} tilføjet";
    public const string AssignmentDeleted = "{0} fjernet";
    public const string AssignmentChanged = "Ansvar ændret: '{0}' → '{1}'";
    public const string AssignmentsAdded = "Tildelte medarbejdere tilføjet";
    public const string AssignmentsDeleted = "Tildelte medarbejdere fjernet";
    public const string AssignmentsChanged = "Tildelte medarbejdere ændret";

    // Worksheet
    public const string WorksheetAdded = "Arbejdsseddel for {0} tilføjet";
    public const string WorksheetDeleted = "Arbejdsseddel for {0} fjernet";

    // Closure flag
    public const string ClosureFlagAdded = "Afslutning af sag {0} tilføjet";
    public const string ClosureFlagDeleted = "Afslutning af {0} fjernet";
    public const string ClosureFlagsAdded = "Afslutningsflag tilføjet";
    public const string ClosureFlagsDeleted = "Afslutningsflag fjernet";
    public const string ClosureFlagsChanged = "Afslutningsflag ændret";

    // Installation type
    public const string InstallationAdded = "Installationstype {0} tilføjet";
    public const string InstallationDeleted = "Installationstype {0} fjernet";
    public const string InstallationModified = "Installationstype {0} opdateret";

    // Category
    public const string CategoryAddedWithType = "Kategori {0} tilføjet til {1}";
    public const string CategoryAdded = "Kategori {0} er tilføjet";
    public const string CategoryDeletedWithType = "Kategori {0} er fjernet fra {1}";
    public const string CategoryDeleted = "Kategori {0} er fjernet";
    public const string CategoryModifiedWithType = "Kategori {0} relevans skiftet til {1}";
    public const string CategoryModified = "Kategori {0} relevans skiftet";

    // Control point
    public const string ControlPointLabel = "Kontrolpunkt {0}";
    public const string OnCategoryAndType = "på {0} ({1})";
    public const string OnType = "på {0}";

    // Job link
    public const string LinkAdded = "Link til {0} tilføjet";
    public const string LinkDeleted = "Link til {0} fjernet";
    public const string LinksAdded = "Relaterede sager tilføjet";
    public const string LinksDeleted = "Relaterede sager fjernet";
    public const string LinksChanged = "Relaterede sager ændret";
}
