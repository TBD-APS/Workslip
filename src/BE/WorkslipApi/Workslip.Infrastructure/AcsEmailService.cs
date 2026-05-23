using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application;

namespace Workslip.Infrastructure;

public sealed class AcsEmailService(
    IConfiguration configuration,
    TokenCredential credential,
    ILogger<AcsEmailService> logger)
    : IEmailService
{
    private readonly string _acsEndpoint = new(
        configuration["Acs:Endpoint"]
        ?? configuration["ACS_ENDPOINT"]
        ?? throw new InvalidOperationException("ACS endpoint is not configured. Set Acs:Endpoint or ACS_ENDPOINT."));

    private readonly string _senderAddress = configuration["Acs:SenderAddress"]
        ?? throw new InvalidOperationException("ACS sender address is not configured. Set Acs:SenderAddress.");

    public async Task SendInviteEmailAsync(string toEmail, string inviteLink, CancellationToken cancellationToken)
    {
        var emailClient = new EmailClient(_acsEndpoint);

        var emailContent = new EmailContent("Du er blevet inviteret til Workslip")
        {
            Html = $"""
            <html>
              <body style="font-family: Arial, sans-serif; padding: 24px;">
                <h2>Velkommen til Workslip</h2>
                <p>Du er blevet inviteret til at deltage i Workslip.</p>
                <p>
                  <a href="{inviteLink}"
                     style="display: inline-block; padding: 12px 24px; background-color: #0057b7; color: #fff; text-decoration: none; border-radius: 6px;">
                    Accepter invitation
                  </a>
                </p>
                <p>Linket udløber om 7 dage.</p>
                <hr/>
                <p style="color: #666; font-size: 12px;">Workslip – automatisk invitation</p>
              </body>
            </html>
            """,
            PlainText = $"""
            Du er blevet inviteret til Workslip.
            Klik på følgende link for at acceptere invitationen:
            {inviteLink}
            Linket udløber om 7 dage.
            """
        };

        var message = new EmailMessage(
            _senderAddress,
            new EmailRecipients([new EmailAddress(toEmail)]),
            emailContent);

        try
        {
            var result = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation("Invite email sent to {Email}. Status: {Status}",
                toEmail, result.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to send invite email to {Email}. ErrorCode: {ErrorCode}", toEmail, ex.ErrorCode);
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

        try
        {
            var result = await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
            logger.LogInformation("OTC email sent to {Email}. Status: {Status}", toEmail, result.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to send OTC email to {Email}. ErrorCode: {ErrorCode}", toEmail, ex.ErrorCode);
            throw;
        }
    }
}
