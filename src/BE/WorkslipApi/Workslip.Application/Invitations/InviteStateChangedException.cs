namespace Workslip.Application.Invitations;

public sealed class InviteStateChangedException(Guid inviteId)
    : Exception($"Invitation state changed before the operation could complete. InviteId: {inviteId}")
{
    public Guid InviteId { get; } = inviteId;
}
