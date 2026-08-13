using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public sealed record UserBillingRateResponse(Guid UserId, decimal? BillableHourlyRate);

public interface IUserBillingRepository
{
    Task<decimal?> GetRateAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task SetRateAsync(Guid organizationId, Guid userId, decimal? rate, CancellationToken cancellationToken);
}

public interface IUserBillingService
{
    Task<Result<UserBillingRateResponse>> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result> UpdateAsync(Guid userId, UpdateBillableHourlyRateRequest request, CancellationToken cancellationToken);
}

public sealed class UserBillingService(
    IUserRepository users,
    IUserBillingRepository billing,
    ICurrentUserContext currentUser,
    ILogger<UserBillingService> logger) : IUserBillingService
{
    private const decimal MaxBillableHourlyRate = 100000m;

    public async Task<Result<UserBillingRateResponse>> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<UserBillingRateResponse>.Unauthorized();

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.OrganizationId != organizationId)
            return Result<UserBillingRateResponse>.NotFound();

        if (!CanManageTarget(user))
            return Result<UserBillingRateResponse>.Forbidden();

        var rate = await billing.GetRateAsync(organizationId, userId, cancellationToken);
        return Result<UserBillingRateResponse>.Success(new UserBillingRateResponse(userId, rate));
    }

    public async Task<Result> UpdateAsync(
        Guid userId,
        UpdateBillableHourlyRateRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result.Unauthorized();

        if (request.BillableHourlyRate is < 0m or > MaxBillableHourlyRate)
        {
            return Result.Invalid(new ValidationError
            {
                Identifier = nameof(UpdateBillableHourlyRateRequest.BillableHourlyRate),
                ErrorMessage = "Den fakturerbare timepris skal være mellem 0 og 100.000 kr."
            });
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.OrganizationId != organizationId)
            return Result.NotFound();

        if (!CanManageTarget(user))
            return Result.Forbidden();

        var rate = request.BillableHourlyRate.HasValue
            ? decimal.Round(request.BillableHourlyRate.Value, 2, MidpointRounding.AwayFromZero)
            : null;

        await billing.SetRateAsync(organizationId, userId, rate, cancellationToken);

        logger.LogInformation(
            "User billing rate updated. UserId: {UserId}. OrganizationId: {OrganizationId}.",
            userId,
            organizationId);

        return Result.NoContent();
    }

    private bool CanManageTarget(UserDataRow user) =>
        !string.Equals(user.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);
}
