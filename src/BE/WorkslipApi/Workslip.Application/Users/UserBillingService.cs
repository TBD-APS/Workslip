using Ardalis.Result;
using Ardalis.Result.FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public sealed record UserBillingRateResponse(Guid UserId, decimal? BillableHourlyRate);

public interface IUserBillingService
{
    Task<Result<UserBillingRateResponse>> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> UpdateAsync(Guid userId, UpdateBillableHourlyRateRequest request, CancellationToken cancellationToken);
}

public sealed class UserBillingService(
    IUserRepository repository,
    ICurrentUserContext currentUser,
    ILogger<UserBillingService> logger) : IUserBillingService
{
    private const decimal MaxBillableHourlyRate = 100000m;

    public async Task<Result<UserBillingRateResponse>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result<UserBillingRateResponse>.Unauthorized();

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result<UserBillingRateResponse>.NotFound();

        if (!CanManageTarget(user))
            return Result<UserBillingRateResponse>.Forbidden();

        return Result<UserBillingRateResponse>.Success(new UserBillingRateResponse(user.Id, user.BillableHourlyRate));
    }

    public async Task<Result> UpdateAsync(
        Guid userId,
        UpdateBillableHourlyRateRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result.Unauthorized();

        if (request.BillableHourlyRate is < 0m or > MaxBillableHourlyRate)
        {
            return Result.Invalid(new ValidationError
            {
                PropertyName = nameof(UpdateBillableHourlyRateRequest.BillableHourlyRate),
                ErrorMessage = "Den fakturerbare timepris skal være mellem 0 og 100.000 kr."
            }.AsValidationError());
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.NotFound();

        if (!CanManageTarget(user))
            return Result.Forbidden();

        var normalizedRate = request.BillableHourlyRate.HasValue
            ? decimal.Round(request.BillableHourlyRate.Value, 2, MidpointRounding.AwayFromZero)
            : null;

        var updated = await repository.SetBillingRateAsync(organizationId.Value, userId, normalizedRate, cancellationToken);
        if (!updated)
            return Result.NotFound();

        logger.LogInformation(
            "User billing rate updated. UserId: {UserId}. OrganizationId: {OrganizationId}.",
            userId,
            organizationId.Value);

        return Result.NoContent();
    }

    private bool CanManageTarget(UserDataRow user) =>
        !string.Equals(user.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);
}
