using Microsoft.Extensions.Logging;

namespace Workslip.Application.Organizations;

public enum OrganizationServiceResultStatus
{
    Success,
    ValidationFailed,
    Conflict,
    NotFound
}

public sealed record OrganizationServiceResult<T>(
    OrganizationServiceResultStatus Status,
    T? Value,
    IReadOnlyList<OrganizationValidationError> Errors,
    string? ErrorCode,
    string? Message)
{
    public static OrganizationServiceResult<T> Success(T value) => new(OrganizationServiceResultStatus.Success, value, [], null, null);
    public static OrganizationServiceResult<T> ValidationFailed(IReadOnlyList<OrganizationValidationError> errors) => new(OrganizationServiceResultStatus.ValidationFailed, default, errors, null, null);
    public static OrganizationServiceResult<T> Conflict(string errorCode, string message) => new(OrganizationServiceResultStatus.Conflict, default, [], errorCode, message);
    public static OrganizationServiceResult<T> NotFound() => new(OrganizationServiceResultStatus.NotFound, default, [], null, null);
}

public interface IOrganizationService
{
    Task<OrganizationServiceResult<OrganizationOnboardingResponse>> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken);
}

public interface IAuthService
{
    Task<OrganizationServiceResult<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class OrganizationService(
    IOrganizationRepository repository,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    public async Task<OrganizationServiceResult<OrganizationOnboardingResponse>> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = OrganizationRequestValidator.ValidateCreate(request);
        if (errors.Count > 0)
        {
            logger.LogWarning("Organization create validation failed. Fields: {ValidationFields}",
                ValidationFields(errors));

            return OrganizationServiceResult<OrganizationOnboardingResponse>.ValidationFailed(errors);
        }

        var normalizedCvr = OrganizationRequestValidator.NormalizeCvr(request.Cvr);
        if (await repository.CvrExistsAsync(normalizedCvr, cancellationToken))
        {
            logger.LogWarning("Organization create conflict. Reason: {Reason}. Cvr: {Cvr}.",
                "organization_cvr_exists",
                normalizedCvr);

            return OrganizationServiceResult<OrganizationOnboardingResponse>.Conflict(
                "organization_cvr_exists",
                "An organization with this CVR already exists.");
        }

        var created = await repository.CreateAsync(request, normalizedCvr, cancellationToken);
        if (created is null)
        {
            logger.LogWarning("Organization create conflict after insert attempt. Reason: {Reason}. Cvr: {Cvr}.",
                "organization_cvr_exists",
                normalizedCvr);

            return OrganizationServiceResult<OrganizationOnboardingResponse>.Conflict(
                "organization_cvr_exists",
                "An organization with this CVR already exists.");
        }

        logger.LogInformation("Organization created. OrganizationId: {OrganizationId}. UserId: {UserId}. Cvr: {Cvr}.",
            created.Organization.Id,
            created.User.Id,
            normalizedCvr);

        return OrganizationServiceResult<OrganizationOnboardingResponse>.Success(created);
    }

    private static string ValidationFields(IEnumerable<OrganizationValidationError> errors) =>
        string.Join(",", errors.Select(error => error.Field).Distinct());
}

public sealed class AuthService(
    IOrganizationRepository repository,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<OrganizationServiceResult<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await repository.GetCurrentUserAsync(userId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Current user lookup returned not found. UserId: {UserId}.", userId);
            return OrganizationServiceResult<CurrentUserResponse>.NotFound();
        }

        logger.LogInformation("Current user fetched. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.",
            user.Id,
            user.Organization.Id,
            user.Role);

        return OrganizationServiceResult<CurrentUserResponse>.Success(user);
    }
}
