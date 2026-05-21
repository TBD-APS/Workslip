namespace Workslip.Application.Jobs;

public static class JobRequestValidator
{
    public static IReadOnlyList<JobValidationError> ValidateCreate(CreateJobRequest request)
    {
        var errors = new List<JobValidationError>();

        Required(request.ReportNumber, "reportNumber", errors);
        Required(request.CustomerName, "customerName", errors);
        Required(request.CustomerAddress, "customerAddress", errors);
        Required(request.TaskDescription, "taskDescription", errors);

        if (request.InstallationTypes.Count == 0)
        {
            errors.Add(new("installationTypes", "Select at least one installation type."));
        }

        Required(request.WorkKind, "workKind", errors);
        if (string.Equals(request.WorkKind, "serviceAndet", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.CustomWorkKind))
        {
            errors.Add(new("customWorkKind", "Custom work kind is required when work kind is Andet."));
        }

        return errors;
    }

    private static void Required(string? value, string field, List<JobValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, $"{field} is required."));
        }
    }
}
