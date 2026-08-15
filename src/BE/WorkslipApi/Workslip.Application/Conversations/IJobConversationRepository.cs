namespace Workslip.Application.Conversations;

public interface IJobConversationRepository
{
    Task<IReadOnlyList<ConversationMessageResponse>> ListAsync(
        Guid organizationId,
        Guid jobId,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<ConversationMessageResponse> CreateAsync(
        Guid organizationId,
        Guid jobId,
        Guid authorUserId,
        string body,
        IReadOnlyList<Guid> mentionedUserIds,
        ConversationActionType? actionType,
        Guid? actionTargetUserId,
        CancellationToken cancellationToken);

    Task<ConversationMessageResponse?> GetByIdAsync(
        Guid organizationId,
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken);

    Task<bool> TryResolveActionAsync(
        Guid organizationId,
        Guid jobId,
        Guid messageId,
        Guid resolverUserId,
        DateTimeOffset resolvedUtc,
        CancellationToken cancellationToken);

    Task MarkReadAsync(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        DateTimeOffset readUtc,
        CancellationToken cancellationToken);
}
