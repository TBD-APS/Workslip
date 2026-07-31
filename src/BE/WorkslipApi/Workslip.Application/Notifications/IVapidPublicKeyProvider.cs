namespace Workslip.Application.Notifications;

public interface IVapidPublicKeyProvider
{
    string PublicKey { get; }
}
