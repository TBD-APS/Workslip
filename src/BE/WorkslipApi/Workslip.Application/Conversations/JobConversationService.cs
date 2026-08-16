using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Common;
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
    IAssignmentRepository assignmentRepository,
    IJobAssignmentService jobAssignmentService,
    ICurrentUserContext currentUser,
    INotificationService notifications,
    IApplicationTransactionFactory transactionFactory) : IJobConversationService
{
    private const int MaxBodyLength = 4000;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private static readonly TimeSpan MaxReminderHorizon = TimeSpan.FromDays(365);

    public async Task<Result<JobConversationResponse>> GetAsync(
        Guid jobId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure<JobConversationResponse>(access);

        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var pageOffset = Math.Max(offset ?? 0, 0);
        var messages = await repository.ListAsync(
            access.Value.OrganizationId,
            jobId,
            pageSize,
            pageOffset,
            cancellationToken);
        var unreadCount = await repository.GetUnreadCountAsync(
            access.Value.OrganizationId,
            jobId,
            access.Value.UserId,
            cancellationToken);

        return Result<JobConversationResponse>.Success(new JobConversationResponse(
            jobId,
            access.Value.Participants.Values
                .Select(ToParticipant)
                .OrderBy(user => user.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            access.Value.AssignableUsers.Values
                .Select(ToParticipant)
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

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length > MaxBodyLength)
            return InvalidMessage(nameof(request.Body), $"Beskeden må højst være {MaxBodyLength} tegn.");

        if (body.Length == 0 && request.ActionType is null)
            return InvalidMessage(nameof(request.Body), "Skriv en besked eller vælg en handling.");

        var mentionedUserIds = (request.MentionedUserIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (mentionedUserIds.Any(id => !access.Value.Participants.ContainsKey(id)))
        {
            return InvalidMessage(
                nameof(request.MentionedUserIds),
                "Du kan kun nævne brugere, der har adgang til denne sag.");
        }

        if ((request.ActionType is null) != (request.ActionTargetUserId is null))
        {
            return InvalidMessage(
                nameof(request.ActionTargetUserId),
                "En handlingsanmodning skal have både handling og modtager.");
        }

        var now = DateTimeOffset.UtcNow;
        AssignedUserResponse? actionRecipient = null;
        if (request.ActionType is ConversationActionType actionType
            && request.ActionTargetUserId is Guid targetUserId)
        {
            var actionValidation = await ValidateActionAsync(
                access.Value,
                actionType,
                targetUserId,
                request.ActionDueUtc,
                body,
                now,
                cancellationToken);
            if (!actionValidation.IsSuccess)
                return MapFailure<ConversationMessageResponse>(actionValidation);

            if (actionType == ConversationActionType.AssignSelf)
            {
                actionRecipient = access.Value.AssignableUsers.GetValueOrDefault(targetUserId);
                if (actionRecipient is null)
                {
                    actionRecipient = (await assignmentRepository.GetAssignedUsersByIdsAsync(
                        access.Value.OrganizationId,
                        [targetUserId],
                        cancellationToken)).SingleOrDefault();
                }
            }
            else
            {
                actionRecipient = access.Value.Participants.GetValueOrDefault(targetUserId);
            }

            if (actionRecipient is null)
                return InvalidMessage(nameof(request.ActionTargetUserId), "Modtageren er ikke længere tilgængelig.");
        }

        await using var transaction = await transactionFactory.BeginTransactionAsync(cancellationToken);
        try
        {
            var message = await repository.CreateAsync(
                access.Value.OrganizationId,
                jobId,
                access.Value.UserId,
                body,
                mentionedUserIds,
                request.ActionType,
                request.ActionTargetUserId,
                request.ActionDueUtc,
                cancellationToken);

            var reportNumber = access.Value.Job.ReportNumber ?? access.Value.Job.Id.ToString("N")[..8];
            var address = access.Value.Job.DestinationAddress
                ?? access.Value.Job.CustomerSnapshot.Address
                ?? "Ingen adresse angivet";
            var actionTargetId = request.ActionTargetUserId;

            if (request.ActionType == ConversationActionType.RemindMe
                && request.ActionDueUtc is DateTimeOffset dueUtc)
            {
                await notifications.QueueConversationReminderAsync(
                    access.Value.UserId,
                    actionRecipient?.DisplayName ?? message.AuthorDisplayName,
                    jobId,
                    reportNumber,
                    address,
                    body.Length == 0 ? $"Påmindelse om SAG-{reportNumber}" : body,
                    message.Id,
                    dueUtc,
                    cancellationToken);
            }
            else if (actionTargetId is Guid actionRecipientId
                && actionRecipientId != access.Value.UserId
                && actionRecipient is not null)
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
                    request.ActionType.Value.ToString(),
                    cancellationToken);
            }

            foreach (var mentionedId in mentionedUserIds)
            {
                if (mentionedId == access.Value.UserId || mentionedId == actionTargetId)
                    continue;

                var recipient = access.Value.Participants[mentionedId];
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

            await transaction.CommitAsync(cancellationToken);
            return Result<ConversationMessageResponse>.Success(message);
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<Result<ConversationMessageResponse>> ResolveActionAsync(
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId
            || currentUser.UserId is not Guid userId)
            return Result<ConversationMessageResponse>.Unauthorized();

        if (!CanUseConversations(currentUser.Role))
            return Result<ConversationMessageResponse>.Forbidden();

        var message = await repository.GetByIdAsync(
            organizationId,
            jobId,
            messageId,
            cancellationToken);
        if (message is null)
            return Result<ConversationMessageResponse>.NotFound();

        if (message.Action is null)
            return Result<ConversationMessageResponse>.Conflict("Beskeden indeholder ingen handling.");

        if (message.Action.Type == ConversationActionType.AssignSelf)
        {
            if (message.Action.TargetUserId != userId)
                return Result<ConversationMessageResponse>.NotFound();

            if (message.Action.Status == ConversationActionStatus.Completed)
                return Result<ConversationMessageResponse>.Success(message);

            return await ResolveAssignSelfAsync(
                organizationId,
                userId,
                jobId,
                messageId,
                cancellationToken);
        }

        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure<ConversationMessageResponse>(access);

        if (message.Action.TargetUserId != userId)
            return Result<ConversationMessageResponse>.Forbidden();

        if (message.Action.Status == ConversationActionStatus.Completed)
            return Result<ConversationMessageResponse>.Success(message);

        if (message.Action.Type == ConversationActionType.RemindMe
            && message.Action.DueUtc is DateTimeOffset dueUtc
            && dueUtc > DateTimeOffset.UtcNow)
        {
            return Result<ConversationMessageResponse>.Conflict("Påmindelsen er ikke udløbet endnu.");
        }

        if (message.Action.Type == ConversationActionType.SubmitForReview)
        {
            if (access.Value.Job.Status is JobStatus.Draft or JobStatus.Rejected)
            {
                var transition = await jobs.ChangeStatusAsync(
                    jobId,
                    new ChangeJobStatusRequest(JobStatus.InReview),
                    cancellationToken);
                if (!transition.IsSuccess)
                    return MapFailure<ConversationMessageResponse>(transition);
            }
            else if (access.Value.Job.Status != JobStatus.InReview)
            {
                return Result<ConversationMessageResponse>.Conflict(
                    "Sagen kan ikke længere sendes til gennemgang fra den aktuelle status.");
            }
        }

        return await CompleteActionAsync(
            organizationId,
            userId,
            jobId,
            messageId,
            cancellationToken);
    }

    public async Task<Result> MarkReadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var access = await GetConversationAccessAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
            return MapFailure(access);

        await repository.MarkReadAsync(
            access.Value.OrganizationId,
            jobId,
            access.Value.UserId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return Result.NoContent();
    }

    private async Task<Result<ConversationMessageResponse>> ResolveAssignSelfAsync(
        Guid organizationId,
        Guid userId,
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await transactionFactory.BeginTransactionAsync(cancellationToken);
        try
        {
            var assignment = await jobAssignmentService.AssignSelfAsync(jobId, cancellationToken);
            if (!assignment.IsSuccess)
            {
                await TryRollbackAsync(transaction, cancellationToken);
                return MapFailure<ConversationMessageResponse>(assignment);
            }

            var result = await CompleteActionAsync(
                organizationId,
                userId,
                jobId,
                messageId,
                cancellationToken);
            if (!result.IsSuccess)
            {
                await TryRollbackAsync(transaction, cancellationToken);
                return result;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task<Result<ConversationMessageResponse>> CompleteActionAsync(
        Guid organizationId,
        Guid userId,
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
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

        if (!resolved && current.Action?.Status != ConversationActionStatus.Completed)
            return Result<ConversationMessageResponse>.Conflict("Handlingen blev ændret af en anden session.");

        return Result<ConversationMessageResponse>.Success(current);
    }

    private async Task<Result> ValidateActionAsync(
        ConversationAccess access,
        ConversationActionType actionType,
        Guid targetUserId,
        DateTimeOffset? dueUtc,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (actionType == ConversationActionType.AssignSelf)
        {
            if (!JobAssignmentPolicy.CanManageAssignments(currentUser.Role))
                return Result.Forbidden();

            if (!access.AssignableUsers.ContainsKey(targetUserId))
                return InvalidAction(nameof(CreateConversationMessageRequest.ActionTargetUserId), "Brugeren kan ikke overtage denne sag.");

            return await jobAssignmentService.ValidateSelfAssignmentTargetAsync(
                access.Job.Id,
                targetUserId,
                cancellationToken);
        }

        if (!access.Participants.ContainsKey(targetUserId))
            return InvalidAction(nameof(CreateConversationMessageRequest.ActionTargetUserId), "Handlingen kan kun sendes til en bruger, der har adgang til denne sag.");

        if (actionType == ConversationActionType.RemindMe)
        {
            if (targetUserId != access.UserId)
                return InvalidAction(nameof(CreateConversationMessageRequest.ActionTargetUserId), "En påmindelse kan kun sættes til dig selv.");

            if (dueUtc is null)
                return InvalidAction(nameof(CreateConversationMessageRequest.ActionDueUtc), "Vælg hvornår Workslip skal minde dig om sagen.");

            if (dueUtc <= now)
                return InvalidAction(nameof(CreateConversationMessageRequest.ActionDueUtc), "Påmindelsen skal ligge i fremtiden.");

            if (dueUtc > now.Add(MaxReminderHorizon))
                return InvalidAction(nameof(CreateConversationMessageRequest.ActionDueUtc), "Påmindelsen kan højst sættes et år frem.");
        }
        else if (dueUtc is not null)
        {
            return InvalidAction(nameof(CreateConversationMessageRequest.ActionDueUtc), "Kun påmindelser kan have et tidspunkt.");
        }

        if (actionType == ConversationActionType.CreateTask && body.Length == 0)
            return InvalidAction(nameof(CreateConversationMessageRequest.Body), "Skriv hvad opgaven går ud på.");

        if (actionType == ConversationActionType.SubmitForReview
            && access.Job.Status is not (JobStatus.Draft or JobStatus.Rejected))
        {
            return Result.Conflict("Sagen kan kun sendes til gennemgang fra kladde eller afvist status.");
        }

        return Result.Success();
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

        var participants = await GetParticipantsAsync(organizationId, job.Value, cancellationToken);
        IReadOnlyDictionary<Guid, AssignedUserResponse> assignableUsers =
            new Dictionary<Guid, AssignedUserResponse>();
        if (JobAssignmentPolicy.CanManageAssignments(currentUser.Role)
            && job.Value.Status != JobStatus.Approved)
        {
            var assignedIds = job.Value.AssignedUsers.Select(user => user.Id).ToHashSet();
            var candidates = await assignmentRepository.GetAssignableUsersForJobAsync(
                organizationId,
                jobId,
                cancellationToken);
            assignableUsers = candidates
                .Where(candidate => candidate.Id != userId && !assignedIds.Contains(candidate.Id))
                .ToDictionary(candidate => candidate.Id);
        }

        return Result<ConversationAccess>.Success(new ConversationAccess(
            organizationId,
            userId,
            job.Value,
            participants,
            assignableUsers));
    }

    private async Task<IReadOnlyDictionary<Guid, AssignedUserResponse>> GetParticipantsAsync(
        Guid organizationId,
        JobReportSummaryResponse job,
        CancellationToken cancellationToken)
    {
        var participants = job.AssignedUsers.ToDictionary(user => user.Id);
        var admins = await assignmentRepository.GetOrganizationAdminsAsync(organizationId, cancellationToken);
        foreach (var admin in admins)
            participants.TryAdd(admin.Id, admin);

        return participants;
    }

    private static ConversationParticipantResponse ToParticipant(AssignedUserResponse user) =>
        new(user.Id, user.DisplayName);

    private static bool CanUseConversations(string? role) =>
        string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

    private static Result<ConversationMessageResponse> InvalidMessage(string identifier, string message) =>
        Result<ConversationMessageResponse>.Invalid([
            new ValidationError { Identifier = identifier, ErrorMessage = message }
        ]);

    private static Result InvalidAction(string identifier, string message) =>
        Result.Invalid([
            new ValidationError { Identifier = identifier, ErrorMessage = message }
        ]);

    private static string GetActionLabel(ConversationActionType type) => type switch
    {
        ConversationActionType.Acknowledge => "Bekræft modtaget",
        ConversationActionType.SubmitForReview => "Send sagen til gennemgang",
        ConversationActionType.CreateTask => "Udfør opgave",
        ConversationActionType.RemindMe => "Påmind mig",
        ConversationActionType.AssignSelf => "Tag sagen",
        _ => "Handling"
    };

    private static async Task TryRollbackAsync(
        IApplicationTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // Preserve the original write/delivery exception. The request is still failed,
            // and endpoint idempotency will not be completed for an uncommitted transaction.
        }
    }

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

    private static Result<T> MapFailure<T>(Result source) => source.Status switch
    {
        ResultStatus.Unauthorized => Result<T>.Unauthorized(),
        ResultStatus.Forbidden => Result<T>.Forbidden(),
        ResultStatus.NotFound => Result<T>.NotFound(),
        ResultStatus.Invalid => Result<T>.Invalid(source.ValidationErrors),
        ResultStatus.Conflict => Result<T>.Conflict(source.Errors.FirstOrDefault() ?? "conversation_action_conflict"),
        ResultStatus.NoContent => Result<T>.Success(default!),
        _ => Result<T>.Error(source.Errors.FirstOrDefault() ?? "conversation_action_failed")
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
        JobReportSummaryResponse Job,
        IReadOnlyDictionary<Guid, AssignedUserResponse> Participants,
        IReadOnlyDictionary<Guid, AssignedUserResponse> AssignableUsers);
}
