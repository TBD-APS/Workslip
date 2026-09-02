using Workslip.Application.Notifications;

namespace Workslip.Tests.Notifications;

internal sealed class NullVapidPublicKeyProvider : IVapidPublicKeyProvider
{
    public string PublicKey { get; } = "BAtesttesttesttesttesttesttesttesttesttesttesttesttesttest";
    public bool IsConfigured { get; } = true;
}