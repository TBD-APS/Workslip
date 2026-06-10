namespace Workslip.Application;

public interface IEmailService
{
    Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken);

    Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken);
}
