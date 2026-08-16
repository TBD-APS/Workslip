namespace Workslip.Application.Conversations;

public enum ConversationActionType
{
    Acknowledge,
    SubmitForReview,
    CreateTask,
    RemindMe,
    AssignSelf
}

public enum ConversationActionStatus
{
    Pending,
    Completed
}

public sealed record ConversationParticipantResponse(
    Guid Id,
    string DisplayName);

public sealed record ConversationActionResponse(
    ConversationActionType Type,
    Guid TargetUserId,
    string TargetDisplayName,
    ConversationActionStatus Status,
    DateTimeOffset? DueUtc,
    Guid? ResolvedByUserId,
    string? ResolvedByDisplayName,
    DateTimeOffset? ResolvedUtc,
    bool CanResolve = true)
{
    public ConversationActionResponse(
        ConversationActionType type,
        Guid targetUserId,
        string targetDisplayName,
        ConversationActionStatus status,
        Guid? resolvedByUserId,
        string? resolvedByDisplayName,
        DateTimeOffset? resolvedUtc)
        : this(
            type,
            targetUserId,
            targetDisplayName,
            status,
            null,
            resolvedByUserId,
            resolvedByDisplayName,
            resolvedUtc,
            true)
    {
    }
}

public sealed record ConversationMessageResponse(
    Guid Id,
    Guid JobId,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string Body,
    IReadOnlyList<Guid> MentionedUserIds,
    ConversationActionResponse? Action,
    DateTimeOffset CreatedUtc);

public sealed record JobConversationResponse(
    Guid JobId,
    IReadOnlyList<ConversationParticipantResponse> Participants,
    IReadOnlyList<ConversationParticipantResponse> AssignableUsers,
    IReadOnlyList<ConversationMessageResponse> Messages,
    int UnreadCount);

public sealed record CreateConversationMessageRequest(
    string? Body,
    IReadOnlyList<Guid>? MentionedUserIds = null,
    ConversationActionType? ActionType = null,
    Guid? ActionTargetUserId = null,
    DateTimeOffset? ActionDueUtc = null);
