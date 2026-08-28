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

        group.MapPost("/webhook/shopify", async (
            HttpContext httpContext,
            IPaymentProvider paymentProvider,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            var signature = httpContext.Request.Headers["X-Shopify-Hmac-Sha256"].ToString();
            using var reader = new StreamReader(httpContext.Request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);

            var tenantId = httpContext.Request.Headers["X-Workslip-Tenant-Id"].FirstOrDefault()
                ?? httpContext.Request.Query["tenant_id"].FirstOrDefault();

            if (string.IsNullOrEmpty(tenantId))
            {
                return Results.BadRequest(new { error = "Missing tenant identification" });
            }

            var handled = await paymentProvider.HandleWebhookAsync(tenantId, payload, signature);

            if (!handled)
            {
                return Results.BadRequest(new { error = "Invalid webhook signature or processing failed" });
            }

            return Results.Ok(new { received = true });
        }).AllowAnonymous()
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(1024 * 1024))
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        var userGroup = group.RequireAuthorization(AuthPolicies.RequireUser);

        userGroup.MapPost("/checkout", async (
            CreateCheckoutRequest request,
            HttpContext httpContext,
            [FromServices] ICurrentUserContext currentUser,
            IPaymentProvider paymentProvider,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            if (currentUser.OrganizationId is not { } tenantId)
            {
                return Results.Unauthorized();
            }

            var returnUrl = request.ReturnUrl ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/payment/success";
            var cancelUrl = request.CancelUrl ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/payment/cancel";

            var metadata = new Dictionary<string, string>
            {
                ["description"] = request.Description ?? "Workslip Payment",
                ["note"] = request.Note ?? string.Empty,
                ["userId"] = currentUser.UserId?.ToString() ?? string.Empty
            };

            if (!string.IsNullOrEmpty(request.Reference))
            {
                metadata["reference"] = request.Reference;
            }

            var checkout = await paymentProvider.CreateCheckoutAsync(
                tenantId.ToString(),
                request.CustomerId ?? string.Empty,
                request.Amount,
                request.Currency,
                returnUrl,
                cancelUrl,
                metadata);

            return Results.Ok(new CreateCheckoutResponse(
                checkout.CheckoutId,
                checkout.CheckoutUrl,
                checkout.OrderId,
                checkout.Amount,
                checkout.Currency,
                checkout.Status));
        }).Produces<CreateCheckoutResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status400BadRequest);

        userGroup.MapGet("/checkout/{checkoutId}/status", async (
            string checkoutId,
            HttpContext httpContext,
            [FromServices] ICurrentUserContext currentUser,
            IPaymentProvider paymentProvider,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            if (currentUser.OrganizationId is not { } tenantId)
            {
                return Results.Unauthorized();
            }

            var result = await paymentProvider.GetPaymentStatusAsync(tenantId.ToString(), checkoutId);

            return Results.Ok(new PaymentStatusResponse(
                result.Success,
                result.OrderId,
                result.TransactionId,
                result.Status,
                result.Amount,
                result.Currency));
        }).Produces<PaymentStatusResponse>()
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    public record CreateCheckoutRequest(
        decimal Amount,
        string Currency = "DKK",
        string? ReturnUrl = null,
        string? CancelUrl = null,
        string? Description = null,
        string? Note = null,
        string? Reference = null,
        string? CustomerId = null);

    public record CreateCheckoutResponse(
        string CheckoutId,
        string CheckoutUrl,
        string OrderId,
        decimal Amount,
        string Currency,
        string Status);

    public record PaymentStatusResponse(
        bool Success,
        string OrderId,
        string TransactionId,
        string Status,
        decimal Amount,
        string Currency);
}