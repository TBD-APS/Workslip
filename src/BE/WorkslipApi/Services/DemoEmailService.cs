using Workslip.Application;

namespace Workslip.Api.Services;

public sealed class DemoEmailService(ILogger<DemoEmailService> logger) : IEmailService
{
    public Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken)
    {
        logger.LogInformation("Demo mode suppressed an invitation email.");
        return Task.CompletedTask;
    }

    public Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken)
    {
        logger.LogInformation("Demo mode suppressed a one-time-code email.");
        return Task.CompletedTask;
    }
}
