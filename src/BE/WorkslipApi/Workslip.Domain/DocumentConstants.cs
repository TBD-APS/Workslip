namespace Workslip.Domain;

public static class ReportStatuses
{
    public const string Draft = "Draft";
    public const string Uploaded = "Uploaded";
    public const string Processing = "Processing";
    public const string InReview = "InReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Archived = "Archived";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Draft,
        Uploaded,
        Processing,
        InReview,
        Approved,
        Rejected,
        Archived,
    };
}

public static class ReviewStatuses
{
    public const string NotStarted = "NotStarted";
    public const string Processing = "Processing";
    public const string NeedsReview = "NeedsReview";
    public const string ReadyForApproval = "ReadyForApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        NotStarted,
        Processing,
        NeedsReview,
        ReadyForApproval,
        Approved,
        Rejected,
    };
}

public static class DocumentFilePurposes
{
    public const string OriginalUpload = "OriginalUpload";
    public const string GeneratedPdf = "GeneratedPdf";
    public const string OcrJson = "OcrJson";
    public const string AiJson = "AiJson";
    public const string Attachment = "Attachment";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        OriginalUpload,
        GeneratedPdf,
        OcrJson,
        AiJson,
        Attachment,
    };
}

public static class FieldDataTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Date = "Date";
    public const string Boolean = "Boolean";
    public const string Choice = "Choice";
    public const string MultiChoice = "MultiChoice";
    public const string Json = "Json";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Text,
        Number,
        Date,
        Boolean,
        Choice,
        MultiChoice,
        Json,
    };
}

public static class FieldStatuses
{
    public const string Confirmed = "Confirmed";
    public const string NeedsReview = "NeedsReview";
    public const string Missing = "Missing";
    public const string Conflict = "Conflict";
    public const string Corrected = "Corrected";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Confirmed,
        NeedsReview,
        Missing,
        Conflict,
        Corrected,
    };
}
