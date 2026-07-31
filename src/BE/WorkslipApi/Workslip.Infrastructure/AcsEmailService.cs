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
    private readonly string _acsEndpoint = new(
        configuration["Azure:Acs:ConnectionString"]
        ?? throw new InvalidOperationException("ACS endpoint is not configured. Set Acs:Endpoint or ACS_ENDPOINT."));

    private readonly string _senderAddress = configuration["Azure:Acs:SenderAddress"]
        ?? throw new InvalidOperationException("ACS sender address is not configured. Set Azure:Acs:SenderAddress.");

    private readonly string _senderPlaínHeaderText = configuration["Azure:Acs:PLainHeaderText"]
    ?? throw new InvalidOperationException("ACS sender address is not configured. Set Azure:Acs:PLainHeaderText.");

    private readonly string _senderPlainText = configuration["Azure:Acs:PlainInviteText"]
        ?? throw new InvalidOperationException("ACS sender address is not configured. Set Azure:Acs:PlainInviteText.");

    private readonly string _senderHtmlText = configuration["Azure:Acs:HtmlInviteText"]
        ?? throw new InvalidOperationException("ACS sender address is not configured. Set Azure:Acs:HtmlInviteText.");

    private readonly string _acsInviteBaseUrlLink = new(
    configuration["Azure:Acs:InviteBaseUrl"]
    ?? throw new InvalidOperationException("ACS endpoint is not configured. Set Acs:Endpoint or ACS_ENDPOINT."));

    private readonly string _otcHeaderText = configuration["Azure:Acs:OtcHeaderText"]
        ?? throw new InvalidOperationException("ACS OTC header text is not configured. Set Azure:Acs:OtcHeaderText.");

    private readonly string _otcPlainText = configuration["Azure:Acs:OtcPlainText"]
        ?? throw new InvalidOperationException("ACS OTC plain text is not configured. Set Azure:Acs:OtcPlainText.");

    private readonly string _otcHtmlText = configuration["Azure:Acs:OtcHtmlText"]
        ?? throw new InvalidOperationException("ACS OTC HTML text is not configured. Set Azure:Acs:OtcHtmlText.");

    public async Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken)
    {
        var emailClient = new EmailClient(_acsEndpoint);

        var trimmedUrl = _acsInviteBaseUrlLink.TrimEnd('/');
        var callBackUrl = $"{trimmedUrl}/{token}";

        var emailContent = new EmailContent(_senderPlaínHeaderText)
        {
            Html = _senderHtmlText.Replace("{inviteLink}", callBackUrl),
            PlainText = _senderPlainText.Replace("{inviteLink}", callBackUrl)
        };

        var message = new EmailMessage(
            _senderAddress,
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
        var emailClient = new EmailClient(_acsEndpoint);

        var emailContent = new EmailContent(_otcHeaderText)
        {
            Html = _otcHtmlText.Replace("{otcCode}", code),
            PlainText = _otcPlainText.Replace("{otcCode}", code)
        };

        var message = new EmailMessage(
            _senderAddress,
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
}
