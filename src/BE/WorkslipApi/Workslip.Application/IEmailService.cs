namespace Workslip.Application;

public interface IEmailService
{
    Task SendInviteEmailAsync(string toEmail, string inviteLink, CancellationToken cancellationToken);

    Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken);
}
