using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Domain;

namespace Workslip.Application.Conversations;

public interface IJobConversationService
{
    Task<Result<JobConversationResponse>> GetAsync(Guid jobId, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<ConversationMessageResponse>> SendAsync(Guid jobId, CreateConversationMessageRequest request, CancellationToken cancellationToken);
    Task<Result<ConversationMessageResponse>> ResolveActionAsync(Guid jobId, Guid messageId, CancellationToken cancellationToken);
    Task<Result> MarkReadAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed class JobConversationService(
    IJobConversationRepository repository,
    IJobService jobs,
    ICurrentUserContext currentUser,
    INotificationService notifications) : IJobConversationService
{
    private const int MaxBodyLength = 4000;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public async Task<Result<JobConversationResponse>> GetAsync(
        Guid jobId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure<JobConversationResponse>(access);

        var (organizationId, userId, job) = access.Value;
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var pageOffset = Math.Max(offset ?? 0, 0);
        var messages = await repository.ListAsync(
            organizationId,
            jobId,
            pageSize,
            pageOffset,
            cancellationToken);
        var unreadCount = await repository.GetUnreadCountAsync(
            organizationId,
            jobId,
            userId,
            cancellationToken);

        return Result<JobConversationResponse>.Success(new JobConversationResponse(
            jobId,
            job.AssignedUsers
                .Select(user => new ConversationParticipantResponse(user.Id, user.DisplayName))
                .OrderBy(user => user.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            messages,
            unreadCount));
    }

    public async Task<Result<ConversationMessageResponse>> SendAsync(
        Guid jobId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure<ConversationMessageResponse>(access);

        var (organizationId, userId, job) = access.Value;
        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length > MaxBodyLength)
        {
            return InvalidMessage(nameof(request.Body), $"Beskeden må højst være {MaxBodyLength} tegn.");
        }

        if (body.Length == 0 && request.ActionType is null)
        {
            return InvalidMessage(nameof(request.Body), "Skriv en besked eller vælg en handling.");
        }

        var participants = job.AssignedUsers.ToDictionary(user => user.Id);
        var mentionedUserIds = (request.MentionedUserIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (mentionedUserIds.Any(id => !participants.ContainsKey(id)))
        {
            return InvalidMessage(
                nameof(request.MentionedUserIds),
                "Du kan kun nævne medarbejdere, der er tildelt denne sag.");
        }

        if ((request.ActionType is null) != (request.ActionTargetUserId is null))
        {
            return InvalidMessage(
                nameof(request.ActionTargetUserId),
                "En handlingsanmodning skal have både handling og modtager.");
        }

        if (request.ActionTargetUserId is Guid targetUserId && !participants.ContainsKey(targetUserId))
        {
            return InvalidMessage(
                nameof(request.ActionTargetUserId),
                "Handlingen kan kun sendes til en medarbejder, der er tildelt denne sag.");
        }

        if (request.ActionType == ConversationActionType.SubmitForReview
            && job.Status is not (JobStatus.Draft or JobStatus.Rejected))
        {
            return Result<ConversationMessageResponse>.Conflict(
                "Sagen kan kun sendes til gennemgang fra kladde eller afvist status.");
        }

        var message = await repository.CreateAsync(
            organizationId,
            jobId,
            userId,
            body,
            mentionedUserIds,
            request.ActionType,
            request.ActionTargetUserId,
            cancellationToken);

        var reportNumber = job.ReportNumber ?? job.Id.ToString("N")[..8];
        var address = job.DestinationAddress ?? job.CustomerSnapshot.Address ?? "Ingen adresse angivet";
        var actionTargetId = request.ActionTargetUserId;

        if (actionTargetId is Guid actionRecipientId
            && actionRecipientId != userId
            && participants.TryGetValue(actionRecipientId, out var actionRecipient))
        {
            await notifications.QueueConversationActionRequestedAsync(
                actionRecipient.Id,
                actionRecipient.DisplayName,
                jobId,
                reportNumber,
                address,
                message.AuthorDisplayName,
                GetActionLabel(request.ActionType!.Value),
                message.Id,
                cancellationToken);
        }

        foreach (var mentionedId in mentionedUserIds)
        {
            if (mentionedId == userId || mentionedId == actionTargetId)
                continue;

            var recipient = participants[mentionedId];
            await notifications.QueueConversationMentionAsync(
                recipient.Id,
                recipient.DisplayName,
                jobId,
                reportNumber,
                address,
                message.AuthorDisplayName,
                message.Id,
                cancellationToken);
        }

        return Result<ConversationMessageResponse>.Success(message);
    }

    public async Task<Result<ConversationMessageResponse>> ResolveActionAsync(
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure<ConversationMessageResponse>(access);

        var (organizationId, userId, job) = access.Value;
        var message = await repository.GetByIdAsync(
            organizationId,
            jobId,
            messageId,
            cancellationToken);
        if (message is null)
            return Result<ConversationMessageResponse>.NotFound();

        if (message.Action is null)
            return Result<ConversationMessageResponse>.Conflict("Beskeden indeholder ingen handling.");

        if (message.Action.TargetUserId != userId)
            return Result<ConversationMessageResponse>.Forbidden();

        if (message.Action.Status == ConversationActionStatus.Completed)
            return Result<ConversationMessageResponse>.Success(message);

        if (message.Action.Type == ConversationActionType.SubmitForReview)
        {
            if (job.Status is JobStatus.Draft or JobStatus.Rejected)
            {
                var transition = await jobs.ChangeStatusAsync(
                    jobId,
                    new ChangeJobStatusRequest(JobStatus.InReview),
                    cancellationToken);
                if (!transition.IsSuccess)
                    return MapFailure<ConversationMessageResponse>(transition);
            }
            else if (job.Status != JobStatus.InReview)
            {
                return Result<ConversationMessageResponse>.Conflict(
                    "Sagen kan ikke længere sendes til gennemgang fra den aktuelle status.");
            }
        }

        var resolved = await repository.TryResolveActionAsync(
            organizationId,
            jobId,
            messageId,
            userId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        var current = await repository.GetByIdAsync(
            organizationId,
            jobId,
            messageId,
            cancellationToken);
        if (current is null)
            return Result<ConversationMessageResponse>.NotFound();

        // A concurrent duplicate tap may lose the conditional update but still observe
        // the already-completed action. Treat that as idempotent success.
        if (!resolved && current.Action?.Status != ConversationActionStatus.Completed)
            return Result<ConversationMessageResponse>.Conflict("Handlingen blev ændret af en anden session.");

        return Result<ConversationMessageResponse>.Success(current);
    }

    public async Task<Result> MarkReadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure(access);

        var (organizationId, userId, _) = access.Value;
        await repository.MarkReadAsync(
            organizationId,
            jobId,
            userId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return Result.NoContent();
    }

    private async Task<Result<ConversationAccess>> GetConversationAccessAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId
            || currentUser.UserId is not Guid userId)
        {
            return Result<ConversationAccess>.Unauthorized();
        }

        if (!CanUseConversations(currentUser.Role))
            return Result<ConversationAccess>.Forbidden();

        var job = await jobs.GetSingleJobAsync(jobId, cancellationToken);
        if (!job.IsSuccess)
            return MapFailure<ConversationAccess>(job);

        return Result<ConversationAccess>.Success(new ConversationAccess(organizationId, userId, job.Value));
    }

    private static bool CanUseConversations(string? role) =>
        string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

    private static Result<ConversationMessageResponse> InvalidMessage(string identifier, string message) =>
        Result<ConversationMessageResponse>.Invalid([
            new ValidationError { Identifier = identifier, ErrorMessage = message }
        ]);

    private static string GetActionLabel(ConversationActionType type) => type switch
    {
        ConversationActionType.Acknowledge => "Bekræft modtaget",
        ConversationActionType.SubmitForReview => "Send sagen til gennemgang",
        _ => "Handling"
    };

    private static Result<T> MapFailure<T>(Result<JobReportSummaryResponse> source) => source.Status switch
    {
        ResultStatus.Unauthorized => Result<T>.Unauthorized(),
        ResultStatus.Forbidden => Result<T>.Forbidden(),
        ResultStatus.NotFound => Result<T>.NotFound(),
        ResultStatus.Invalid => Result<T>.Invalid(source.ValidationErrors),
        ResultStatus.Conflict => Result<T>.Conflict(source.Errors.FirstOrDefault() ?? "job_conflict"),
        _ => Result<T>.Error(source.Errors.FirstOrDefault() ?? "job_access_failed")
    };

    private static Result<T> MapFailure<T>(Result<ConversationAccess> source) => source.Status switch
    {
        ResultStatus.Unauthorized => Result<T>.Unauthorized(),
        ResultStatus.Forbidden => Result<T>.Forbidden(),
        ResultStatus.NotFound => Result<T>.NotFound(),
        ResultStatus.Invalid => Result<T>.Invalid(source.ValidationErrors),
        ResultStatus.Conflict => Result<T>.Conflict(source.Errors.FirstOrDefault() ?? "conversation_conflict"),
        _ => Result<T>.Error(source.Errors.FirstOrDefault() ?? "conversation_access_failed")
    };

    private static Result MapFailure(Result<ConversationAccess> source) => source.Status switch
    {
        ResultStatus.Unauthorized => Result.Unauthorized(),
        ResultStatus.Forbidden => Result.Forbidden(),
        ResultStatus.NotFound => Result.NotFound(),
        ResultStatus.Invalid => Result.Invalid(source.ValidationErrors),
        ResultStatus.Conflict => Result.Conflict(source.Errors.FirstOrDefault() ?? "conversation_conflict"),
        _ => Result.Error(source.Errors.FirstOrDefault() ?? "conversation_access_failed")
    };

    private sealed record ConversationAccess(
        Guid OrganizationId,
        Guid UserId,
        JobReportSummaryResponse Job);
}
