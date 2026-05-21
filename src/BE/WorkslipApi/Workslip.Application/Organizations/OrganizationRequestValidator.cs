namespace Workslip.Application.Organizations;

public static class OrganizationRequestValidator
{
    public static IReadOnlyList<OrganizationValidationError> ValidateCreate(CreateOrganizationRequest request)
    {
        var errors = new List<OrganizationValidationError>();

        Required(request.Name, "name", errors);
        Required(request.Cvr, "cvr", errors);
        Required(request.AdminDisplayName, "adminDisplayName", errors);

        var normalizedCvr = NormalizeCvr(request.Cvr);
        if (!string.IsNullOrWhiteSpace(request.Cvr) && normalizedCvr.Length != 8)
        {
            errors.Add(new("cvr", "CVR must contain exactly 8 digits."));
        }

        if (!string.IsNullOrWhiteSpace(request.AdminEmail) && !request.AdminEmail.Contains('@', StringComparison.Ordinal))
        {
            errors.Add(new("adminEmail", "Admin email must be a valid email address."));
        }

        return errors;
    }

    public static string NormalizeCvr(string cvr) =>
        new(cvr.Where(char.IsDigit).ToArray());

    private static void Required(string? value, string field, List<OrganizationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, $"{field} is required."));
        }
    }
}
