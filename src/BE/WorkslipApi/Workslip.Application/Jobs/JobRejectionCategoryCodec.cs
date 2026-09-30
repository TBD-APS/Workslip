namespace Workslip.Application.Jobs;

public static class JobRejectionCategoryCodec
{
    private const string Prefix = "[workslip-rejection:";

    private static readonly HashSet<string> KnownCategories = new(StringComparer.Ordinal)
    {
        "missing_required_data",
        "incorrect_measurement_or_value",
        "missing_photo_documentation",
        "wrong_control_point",
        "incomplete_work",
        "duplicate_or_wrong_job",
        "system_or_ui_issue",
        "other"
    };

    public static DecodedRejection Decode(string? rejectionNote)
    {
        if (string.IsNullOrWhiteSpace(rejectionNote))
            return new DecodedRejection(null, rejectionNote);

        var trimmed = rejectionNote.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return new DecodedRejection(null, trimmed);

        var markerEnd = trimmed.IndexOf(']');
        if (markerEnd <= Prefix.Length)
            return new DecodedRejection(null, trimmed);

        var category = trimmed[Prefix.Length..markerEnd];
        if (!KnownCategories.Contains(category))
            return new DecodedRejection(null, trimmed[(markerEnd + 1)..].TrimStart());

        var note = trimmed[(markerEnd + 1)..].TrimStart();
        return new DecodedRejection(category, note);
    }

    public sealed record DecodedRejection(string? Category, string? Note);
}
