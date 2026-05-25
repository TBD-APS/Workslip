using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Workslip.Application.Organizations;

public interface IOrganizationService
{
    Task<Result<OrganizationOnboardingResponse>> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken);
}

public sealed class OrganizationService(
    IOrganizationRepository repository,
    IValidator<CreateOrganizationRequest> createOrganizationValidator,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    public async Task<Result<OrganizationOnboardingResponse>> CreateAsync(CreateOrganizationRequest request,CancellationToken cancellationToken)
    {
        var validationResult = await createOrganizationValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError
                {
                    Identifier = e.PropertyName,
                    ErrorMessage = e.ErrorMessage
                })
                .ToList();

            logger.LogWarning("Organization create validation failed. Fields: {ValidationFields}", ValidationFields(errors));

            return Result<OrganizationOnboardingResponse>.Invalid(errors);
        }

        var normalizedCvr = OrganizationRequestValidator.NormalizeCvr(request.Cvr);
        if (await repository.CvrExistsAsync(normalizedCvr, cancellationToken))
        {
            logger.LogWarning("Organization create conflict. Reason: {Reason}. Cvr: {Cvr}.", "organization_cvr_exists", normalizedCvr);
            return Result<OrganizationOnboardingResponse>.Conflict("organization_cvr_exists");
        }

        var created = await repository.CreateAsync(request, normalizedCvr, cancellationToken);
        if (created is null)
        {
            logger.LogWarning("Organization create conflict after insert attempt. Cvr: {Cvr}.", normalizedCvr);

            return Result<OrganizationOnboardingResponse>.Conflict("organization_cvr_exists");
        }

        logger.LogInformation("Organization created. OrganizationId: {OrganizationId}. UserId: {UserId}. Cvr: {Cvr}.",
            created.Organization.Id,
            created.User.Id,
            normalizedCvr);

        return Result<OrganizationOnboardingResponse>.Success(created);
    }

    private static string ValidationFields(IEnumerable<ValidationError> errors) =>
        string.Join(",", errors.Select(error => error.Identifier).Distinct());
}
