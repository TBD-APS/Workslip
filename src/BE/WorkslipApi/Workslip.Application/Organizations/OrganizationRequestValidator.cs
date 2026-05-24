namespace Workslip.Application.Organizations;

public static class OrganizationRequestValidator
{
    public static string NormalizeCvr(string cvr) =>
        new(cvr.Where(char.IsDigit).ToArray());
}
