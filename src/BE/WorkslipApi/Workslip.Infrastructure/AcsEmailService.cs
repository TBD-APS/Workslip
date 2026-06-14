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

        var startTime = DateTimeOffset.UtcNow;
        logger.LogInformation("ACS sending invite email. CorrelationId={CorrelationId} To={Email} Sender={Sender}", correlationIdAccessor.CorrelationId, toEmail, _senderAddress);

        try
        {
            var result = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation("ACS invite email sent. CorrelationId={CorrelationId} To={Email} Status={Status}", correlationIdAccessor.CorrelationId, toEmail, result.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "ACS invite email failed. CorrelationId={CorrelationId} To={Email} ErrorCode={ErrorCode}", correlationIdAccessor.CorrelationId, toEmail, ex.ErrorCode);
            throw;
        }
    }

    public async Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken)
    {
        var emailClient = new EmailClient(_acsEndpoint);

        var emailContent = new EmailContent("Din midlertidige adgangskode til Workslip")
        {
            Html = $"""
            <html>
              <body style="font-family: Arial, sans-serif; padding: 24px;">
                <h2>Midlertidig adgangskode</h2>
                <p>Du har anmodet om en midlertidig adgangskode til Workslip.</p>
                <p style="font-size: 32px; font-weight: bold; letter-spacing: 8px; text-align: center; padding: 16px; background-color: #f5f5f5; border-radius: 8px;">
                  {code}
                </p>
                <p>Koden udløber om 10 minutter.</p>
                <p>Hvis du ikke har bedt om denne kode, kan du ignorere denne email.</p>
                <hr/>
                <p style="color: #666; font-size: 12px;">Workslip – midlertidig adgangskode</p>
              </body>
            </html>
            """,
            PlainText = $"""
            Din midlertidige adgangskode til Workslip: {code}
            Koden udløber om 10 minutter.
            Hvis du ikke har bedt om denne kode, kan du ignorere denne email.
            """
        };

        var message = new EmailMessage(
            _senderAddress,
            new EmailRecipients([new EmailAddress(toEmail)]),
            emailContent);

        logger.LogInformation("ACS sending OTC email. CorrelationId={CorrelationId} To={Email} Sender={Sender}",correlationIdAccessor.CorrelationId, toEmail, _senderAddress);

        try
        {
            var result = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation("ACS OTC email sent. CorrelationId={CorrelationId} To={Email} Status={Status}", correlationIdAccessor.CorrelationId, toEmail, result.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex,"ACS OTC email failed. CorrelationId={CorrelationId} To={Email} ErrorCode={ErrorCode}", correlationIdAccessor.CorrelationId, toEmail, ex.ErrorCode);
            throw;
        }
    }
}
