using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Application.Auth;
using Workslip.Application.Integrations;

namespace Workslip.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payments").WithTags("payments");

        var userGroup = group.RequireAuthorization(AuthPolicies.RequireUser);

        userGroup.MapPost("/checkout", async (
            CreateCheckoutRequest request,
            HttpContext httpContext,
            [FromServices] ICurrentUserContext currentUser,
            [FromServices] IPaymentProvider paymentProvider) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            if (currentUser.OrganizationId is not { } tenantId)
            {
                return Results.Unauthorized();
            }

            if (request.Quantity is < 1 or > 100)
            {
                return Results.BadRequest(new { error = "Quantity must be between 1 and 100." });
            }

            try
            {
                var checkout = await paymentProvider.CreateCheckoutAsync(
                    tenantId.ToString(),
                    request.Quantity,
                    httpContext.Connection.RemoteIpAddress?.ToString());

                return Results.Ok(new CreateCheckoutResponse(
                    checkout.CheckoutUrl,
                    checkout.Amount,
                    checkout.Currency));
            }
            catch (PaymentProviderConfigurationException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Hosted checkout is not available.");
            }
        }).Produces<CreateCheckoutResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    public record CreateCheckoutRequest(int Quantity = 1);

    public record CreateCheckoutResponse(
        string CheckoutUrl,
        decimal Amount,
        string Currency);
}
