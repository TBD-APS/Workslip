using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application;

namespace Workslip.Infrastructure;

public sealed class AcsEmailService(
    IConfiguration configuration,
    ILogger<AcsEmailService> logger,
    ICorrelationIdAccessor correlationIdAccessor)
    : IEmailService
{
    private readonly string? _acsConnectionString = configuration["Azure:Acs:ConnectionString"];
    private readonly string? _senderAddress = configuration["Azure:Acs:SenderAddress"];
    private readonly string? _senderPlainHeaderText = configuration["Azure:Acs:PLainHeaderText"];
    private readonly string? _senderPlainText = configuration["Azure:Acs:PlainInviteText"];
    private readonly string? _senderHtmlText = configuration["Azure:Acs:HtmlInviteText"];
    private readonly string? _acsInviteBaseUrlLink = configuration["Azure:Acs:InviteBaseUrl"];
    private readonly string? _otcHeaderText = configuration["Azure:Acs:OtcHeaderText"];
    private readonly string? _otcPlainText = configuration["Azure:Acs:OtcPlainText"];
    private readonly string? _otcHtmlText = configuration["Azure:Acs:OtcHtmlText"];

    public async Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken)
    {
        if (SkipBecauseAcsIsNotConfiguredInDevelopment("invite"))
            return;

        var emailClient = new EmailClient(RequireConfigured(_acsConnectionString, "Azure:Acs:ConnectionString"));
        var senderAddress = RequireConfigured(_senderAddress, "Azure:Acs:SenderAddress");
        var headerText = RequireConfigured(_senderPlainHeaderText, "Azure:Acs:PLainHeaderText");
        var plainText = RequireConfigured(_senderPlainText, "Azure:Acs:PlainInviteText");
        var htmlText = RequireConfigured(_senderHtmlText, "Azure:Acs:HtmlInviteText");
        var inviteBaseUrl = RequireConfigured(_acsInviteBaseUrlLink, "Azure:Acs:InviteBaseUrl");

        var trimmedUrl = inviteBaseUrl.TrimEnd('/');
        var callBackUrl = $"{trimmedUrl}/{token}";

        var emailContent = new EmailContent(headerText)
        {
            Html = htmlText.Replace("{inviteLink}", callBackUrl),
            PlainText = plainText.Replace("{inviteLink}", callBackUrl)
        };

        var message = new EmailMessage(
            senderAddress,
            new EmailRecipients([new EmailAddress(toEmail)]),
            emailContent);

        logger.LogInformation("ACS sending invite email. CorrelationId={CorrelationId}", correlationIdAccessor.CorrelationId);

        try
        {
            var result = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation(
                "ACS invite email sent. CorrelationId={CorrelationId} Status={Status}",
                correlationIdAccessor.CorrelationId,
                result.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(
                ex,
                "ACS invite email failed. CorrelationId={CorrelationId} ErrorCode={ErrorCode}",
                correlationIdAccessor.CorrelationId,
                ex.ErrorCode);
            throw;
        }
    }

    public async Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken)
    {
        if (SkipBecauseAcsIsNotConfiguredInDevelopment("OTC"))
            return;

        var emailClient = new EmailClient(RequireConfigured(_acsConnectionString, "Azure:Acs:ConnectionString"));
        var senderAddress = RequireConfigured(_senderAddress, "Azure:Acs:SenderAddress");
        var headerText = RequireConfigured(_otcHeaderText, "Azure:Acs:OtcHeaderText");
        var plainText = RequireConfigured(_otcPlainText, "Azure:Acs:OtcPlainText");
        var htmlText = RequireConfigured(_otcHtmlText, "Azure:Acs:OtcHtmlText");

        var emailContent = new EmailContent(headerText)
        {
            Html = htmlText.Replace("{otcCode}", code),
            PlainText = plainText.Replace("{otcCode}", code)
        };

        var message = new EmailMessage(
            senderAddress,
            new EmailRecipients([new EmailAddress(toEmail)]),
            emailContent);

        logger.LogInformation("ACS sending OTC email. CorrelationId={CorrelationId}", correlationIdAccessor.CorrelationId);

        try
        {
            var result = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation(
                "ACS OTC email sent. CorrelationId={CorrelationId} Status={Status}",
                correlationIdAccessor.CorrelationId,
                result.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(
                ex,
                "ACS OTC email failed. CorrelationId={CorrelationId} ErrorCode={ErrorCode}",
                correlationIdAccessor.CorrelationId,
                ex.ErrorCode);
            throw;
        }
    }

    private bool SkipBecauseAcsIsNotConfiguredInDevelopment(string emailKind)
    {
        if (!string.IsNullOrWhiteSpace(_acsConnectionString))
            return false;

        if (!string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"],
            "Development",
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        logger.LogInformation(
            "ACS is not configured; skipping {EmailKind} email send in Development. CorrelationId={CorrelationId}",
            emailKind,
            correlationIdAccessor.CorrelationId);

        return true;
    }

    private static string RequireConfigured(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"ACS configuration '{key}' is not configured.");
}
