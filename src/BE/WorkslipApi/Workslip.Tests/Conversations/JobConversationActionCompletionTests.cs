using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Conversations;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Tests.Conversations;

public sealed class JobConversationActionCompletionTests
{
    [Fact]
    public async Task SendAsync_create_task_requires_description()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = new RecordingConversationRepository();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")])),
            new RecordingAssignmentRepository(),
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin),
            new RecordingNotificationService());

        var result = await service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                null,
                [],
                ConversationActionType.CreateTask,
                targetId),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task SendAsync_remind_me_persists_due_time_and_schedules_notification()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dueUtc = DateTimeOffset.UtcNow.AddHours(2);
        var repository = new RecordingConversationRepository { TargetDisplayName = "Rasmus" };
        var notifications = new RecordingNotificationService();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(jobId, organizationId, [new(userId, "Rasmus")])),
            new RecordingAssignmentRepository(),
            new TestCurrentUserContext(userId, organizationId, Roles.User),
            notifications);

        var result = await service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                "Husk at ringe kunden",
                [],
                ConversationActionType.RemindMe,
                userId,
                dueUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(dueUtc, result.Value.Action?.DueUtc);
        var reminder = Assert.Single(notifications.Reminders);
        Assert.Equal(userId, reminder.UserId);
        Assert.Equal(jobId, reminder.JobId);
        Assert.Equal(dueUtc, reminder.DueUtc);
        Assert.Equal("Husk at ringe kunden", reminder.ReminderText);
    }

    [Fact]
    public async Task SendAsync_remind_me_rejects_other_recipient_and_past_due_time()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var repository = new RecordingConversationRepository();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(
                jobId,
                organizationId,
                [new(userId, "Rasmus"), new(otherId, "Mikkel")])),
            new RecordingAssignmentRepository(),
            new TestCurrentUserContext(userId, organizationId, Roles.User),
            new RecordingNotificationService());

        var otherRecipient = await service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                "Husk det",
                [],
                ConversationActionType.RemindMe,
                otherId,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        var pastDue = await service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                "Husk det",
                [],
                ConversationActionType.RemindMe,
                userId,
                DateTimeOffset.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, otherRecipient.Status);
        Assert.Equal(ResultStatus.Invalid, pastDue.Status);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task SendAsync_assign_self_accepts_eligible_unassigned_target_without_granting_conversation_access()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = new RecordingConversationRepository { TargetDisplayName = "Mikkel" };
        var assignments = new RecordingAssignmentRepository
        {
            Admins = [new AssignedUserResponse(adminId, "Admin")],
            AssignableUsers = [new AssignedUserResponse(targetId, "Mikkel")]
        };
        var jobAssignment = new RecordingJobAssignmentService();
        var notifications = new RecordingNotificationService();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(jobId, organizationId, [])),
            assignments,
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin),
            notifications,
            jobAssignment);

        var result = await service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                "Kan du tage den?",
                [],
                ConversationActionType.AssignSelf,
                targetId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetId, result.Value.Action?.TargetUserId);
        Assert.Equal(1, jobAssignment.ValidateTargetCalls);
        var action = Assert.Single(notifications.Actions);
        Assert.Equal(targetId, action.UserId);
        Assert.Equal("Tag sagen", action.ActionLabel);
    }

    [Fact]
    public async Task ResolveAction_assign_self_allows_target_before_normal_job_access()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var repository = new RecordingConversationRepository
        {
            ExistingMessage = PendingActionMessage(
                messageId,
                jobId,
                targetId,
                ConversationActionType.AssignSelf)
        };
        var jobs = new RecordingJobService(null)
        {
            GetResult = Result<JobReportSummaryResponse>.NotFound()
        };
        var jobAssignment = new RecordingJobAssignmentService();
        var service = CreateService(
            repository,
            jobs,
            new RecordingAssignmentRepository(),
            new TestCurrentUserContext(targetId, organizationId, Roles.User),
            new RecordingNotificationService(),
            jobAssignment);

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, jobAssignment.AssignSelfCalls);
        Assert.Equal(1, repository.ResolveCalls);
        Assert.Equal(ConversationActionStatus.Completed, result.Value.Action?.Status);
    }

    [Fact]
    public async Task ResolveAction_assign_self_returns_not_found_to_non_target()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var repository = new RecordingConversationRepository
        {
            ExistingMessage = PendingActionMessage(
                messageId,
                jobId,
                targetId,
                ConversationActionType.AssignSelf)
        };
        var jobAssignment = new RecordingJobAssignmentService();
        var service = CreateService(
            repository,
            new RecordingJobService(null),
            new RecordingAssignmentRepository(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.User),
            new RecordingNotificationService(),
            jobAssignment);

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, jobAssignment.AssignSelfCalls);
        Assert.Equal(0, repository.ResolveCalls);
    }

    [Fact]
    public async Task ResolveAction_reminder_cannot_complete_before_due_time()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var repository = new RecordingConversationRepository
        {
            ExistingMessage = PendingActionMessage(
                messageId,
                jobId,
                userId,
                ConversationActionType.RemindMe,
                DateTimeOffset.UtcNow.AddHours(1))
        };
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(jobId, organizationId, [new(userId, "Rasmus")])),
            new RecordingAssignmentRepository(),
            new TestCurrentUserContext(userId, organizationId, Roles.User),
            new RecordingNotificationService());

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0, repository.ResolveCalls);
    }

    private static JobConversationService CreateService(
        IJobConversationRepository repository,
        IJobService jobs,
        IAssignmentRepository assignments,
        ICurrentUserContext currentUser,
        INotificationService notifications,
        IJobAssignmentService? jobAssignmentService = null) =>
        new(
            repository,
            jobs,
            assignments,
            currentUser,
            notifications,
            new RecordingTransactionFactory(),
            jobAssignmentService);

    private static ConversationMessageResponse PendingActionMessage(
        Guid messageId,
        Guid jobId,
        Guid targetId,
        ConversationActionType type,
        DateTimeOffset? dueUtc = null) =>
        new(
            messageId,
            jobId,
            Guid.NewGuid(),
            "Admin",
            "Handling",
            [],
            new ConversationActionResponse(
                type,
                targetId,
                "Mikkel",
                ConversationActionStatus.Pending,
                dueUtc,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);

    private static JobReportSummaryResponse CreateSummary(
        Guid jobId,
        Guid organizationId,
        IReadOnlyList<AssignedUserResponse> assignedUsers,
        JobStatus status = JobStatus.Draft) =>
        new(
            jobId,
            organizationId,
            "Test organization",
            "12345678",
            "R-1",
            status,
            null,
            new CustomerSnapshotResponse("Kunde", null, null, "Kundevej 1", null),
            "Jobvej 2",
            null,
            null,
            JobType.Diverse.ToString(),
            new JobReportSummaryWorkResponse(null, [], [], null),
            new JobReportSummaryObservationResponse(null, null, null),
            [],
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            assignedUsers,
            [],
            null,
            null,
            false,
            null);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingTransactionFactory : IApplicationTransactionFactory
    {
        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IApplicationTransaction>(new RecordingTransaction());
    }

    private sealed class RecordingTransaction : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingAssignmentRepository : IAssignmentRepository
    {
        public IReadOnlyList<AssignedUserResponse> Admins { get; init; } = [];
        public IReadOnlyList<AssignedUserResponse> AssignableUsers { get; init; } = [];

        public Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Admins);

        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignableUsersForJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(AssignableUsers);

        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedUserResponse>>(
                AssignableUsers.Where(user => userIds.Contains(user.Id)).ToArray());

        public Task AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingConversationRepository : IJobConversationRepository
    {
        public int CreateCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public string TargetDisplayName { get; init; } = "Medarbejder";
        public ConversationMessageResponse? ExistingMessage { get; set; }

        public Task<IReadOnlyList<ConversationMessageResponse>> ListAsync(Guid organizationId, Guid jobId, int limit, int offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConversationMessageResponse>>(ExistingMessage is null ? [] : [ExistingMessage]);

        public Task<int> GetUnreadCountAsync(Guid organizationId, Guid jobId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<ConversationMessageResponse> CreateAsync(
            Guid organizationId,
            Guid jobId,
            Guid authorUserId,
            string body,
            IReadOnlyList<Guid> mentionedUserIds,
            ConversationActionType? actionType,
            Guid? actionTargetUserId,
            DateTimeOffset? actionDueUtc,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            ExistingMessage = new ConversationMessageResponse(
                Guid.NewGuid(),
                jobId,
                authorUserId,
                "Admin",
                body,
                mentionedUserIds,
                actionType is null || actionTargetUserId is null
                    ? null
                    : new ConversationActionResponse(
                        actionType.Value,
                        actionTargetUserId.Value,
                        TargetDisplayName,
                        ConversationActionStatus.Pending,
                        actionDueUtc,
                        null,
                        null,
                        null),
                DateTimeOffset.UtcNow);
            return Task.FromResult(ExistingMessage);
        }

        public Task<ConversationMessageResponse?> GetByIdAsync(Guid organizationId, Guid jobId, Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingMessage);

        public Task<bool> TryResolveActionAsync(Guid organizationId, Guid jobId, Guid messageId, Guid resolverUserId, DateTimeOffset resolvedUtc, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            if (ExistingMessage?.Action is not { Status: ConversationActionStatus.Pending } action
                || action.TargetUserId != resolverUserId)
            {
                return Task.FromResult(false);
            }

            ExistingMessage = ExistingMessage with
            {
                Action = action with
                {
                    Status = ConversationActionStatus.Completed,
                    ResolvedByUserId = resolverUserId,
                    ResolvedByDisplayName = action.TargetDisplayName,
                    ResolvedUtc = resolvedUtc
                }
            };
            return Task.FromResult(true);
        }

        public Task MarkReadAsync(Guid organizationId, Guid jobId, Guid userId, DateTimeOffset readUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingJobAssignmentService : IJobAssignmentService
    {
        public int ValidateTargetCalls { get; private set; }
        public int AssignSelfCalls { get; private set; }
        public Result ValidateTargetResult { get; init; } = Result.Success();
        public Result AssignSelfResult { get; init; } = Result.NoContent();

        public Task<Result<JobReportSummaryResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> ValidateSelfAssignmentTargetAsync(Guid jobId, Guid targetUserId, CancellationToken cancellationToken)
        {
            ValidateTargetCalls++;
            return Task.FromResult(ValidateTargetResult);
        }

        public Task<Result> AssignSelfAsync(Guid jobId, CancellationToken cancellationToken)
        {
            AssignSelfCalls++;
            return Task.FromResult(AssignSelfResult);
        }
    }

    private sealed class RecordingJobService(JobReportSummaryResponse? summary) : IJobService
    {
        public Result<JobReportSummaryResponse>? GetResult { get; init; }

        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult ?? (summary is null
                ? Result<JobReportSummaryResponse>.NotFound()
                : Result<JobReportSummaryResponse>.Success(summary)));

        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(summary is null
                ? Result<JobReportSummaryResponse>.NotFound()
                : Result<JobReportSummaryResponse>.Success(summary));

        public Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobListResponse>> ListAsync(List<JobStatus>? statuses, string? reportNumber, string? customerName, string? customerEmail, string? customerAddress, string? search, string? sortBy, string? sortDirection, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> MarkJobAsSeenAsync(Guid id, string? viewType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateJobDetailCacheAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<ActionCall> Actions { get; } = [];
        public List<ReminderCall> Reminders { get; } = [];

        public Task QueueConversationActionRequestedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string actorName, string actionLabel, Guid messageId, CancellationToken cancellationToken)
        {
            Actions.Add(new ActionCall(userId, jobId, messageId, actionLabel));
            return Task.CompletedTask;
        }

        public Task QueueConversationReminderAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string reminderText, Guid messageId, DateTimeOffset dueUtc, CancellationToken cancellationToken)
        {
            Reminders.Add(new ReminderCall(userId, jobId, messageId, dueUtc, reminderText));
            return Task.CompletedTask;
        }

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.NoContent());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => ("", "");
    }

    private sealed record ActionCall(Guid UserId, Guid JobId, Guid MessageId, string ActionLabel);
    private sealed record ReminderCall(Guid UserId, Guid JobId, Guid MessageId, DateTimeOffset DueUtc, string ReminderText);
}
