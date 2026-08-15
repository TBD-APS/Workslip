using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Conversations;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Conversations;

public sealed class JobConversationServiceTests
{
    [Fact]
    public async Task SendAsync_rejects_mentions_outside_assigned_job_participants()
    {
        var organizationId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(Guid.NewGuid(), organizationId, [new(assigneeId, "Montør")])),
            new TestCurrentUserContext(authorId, organizationId, Roles.Admin),
            new RecordingNotificationService());

        var result = await service.SendAsync(
            Guid.NewGuid(),
            new CreateConversationMessageRequest("Hej", [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task SendAsync_action_target_gets_one_action_notification_even_when_also_mentioned()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = new RecordingRepository { TargetDisplayName = "Mikkel" };
        var notifications = new RecordingNotificationService();
        var transactionFactory = new RecordingTransactionFactory();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")])),
            new TestCurrentUserContext(authorId, organizationId, Roles.Admin),
            notifications,
            transactionFactory);

        var result = await service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                "Kan du sende den til gennemgang?",
                [targetId],
                ConversationActionType.SubmitForReview,
                targetId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(notifications.Actions);
        Assert.Empty(notifications.Mentions);
        Assert.Equal(targetId, notifications.Actions[0].UserId);
        Assert.Equal("Send sagen til gennemgang", notifications.Actions[0].ActionLabel);
        Assert.Equal(1, transactionFactory.Transaction.CommitCalls);
        Assert.Equal(0, transactionFactory.Transaction.RollbackCalls);
    }

    [Fact]
    public async Task SendAsync_rolls_back_message_when_notification_queue_fails()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = new RecordingRepository { TargetDisplayName = "Mikkel" };
        var notifications = new RecordingNotificationService { ThrowOnAction = true };
        var transactionFactory = new RecordingTransactionFactory();
        var service = CreateService(
            repository,
            new RecordingJobService(CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")])),
            new TestCurrentUserContext(authorId, organizationId, Roles.Admin),
            notifications,
            transactionFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(
            jobId,
            new CreateConversationMessageRequest(
                "Kan du bekræfte?",
                [targetId],
                ConversationActionType.Acknowledge,
                targetId),
            CancellationToken.None));

        Assert.Equal(1, repository.CreateCalls);
        Assert.Equal(0, transactionFactory.Transaction.CommitCalls);
        Assert.Equal(1, transactionFactory.Transaction.RollbackCalls);
    }

    [Fact]
    public async Task ResolveAction_submit_for_review_reuses_job_transition_before_completing_action()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var summary = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")]);
        var jobs = new RecordingJobService(summary);
        var repository = new RecordingRepository
        {
            ExistingMessage = PendingActionMessage(messageId, jobId, targetId, ConversationActionType.SubmitForReview)
        };
        var service = CreateService(
            repository,
            jobs,
            new TestCurrentUserContext(targetId, organizationId, Roles.User),
            new RecordingNotificationService());

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.InReview, jobs.LastStatusRequest?.Status);
        Assert.Equal(1, repository.ResolveCalls);
        Assert.Equal(ConversationActionStatus.Completed, result.Value.Action?.Status);
    }

    [Fact]
    public async Task ResolveAction_recovers_when_job_transition_already_succeeded_but_action_is_still_pending()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var summary = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")], JobStatus.InReview);
        var jobs = new RecordingJobService(summary);
        var repository = new RecordingRepository
        {
            ExistingMessage = PendingActionMessage(messageId, jobId, targetId, ConversationActionType.SubmitForReview)
        };
        var service = CreateService(
            repository,
            jobs,
            new TestCurrentUserContext(targetId, organizationId, Roles.User),
            new RecordingNotificationService());

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(jobs.LastStatusRequest);
        Assert.Equal(1, repository.ResolveCalls);
        Assert.Equal(ConversationActionStatus.Completed, result.Value.Action?.Status);
    }

    [Fact]
    public async Task ResolveAction_rejects_non_target_without_executing_domain_action()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var jobs = new RecordingJobService(CreateSummary(
            jobId,
            organizationId,
            [new(targetId, "Mikkel"), new(otherUserId, "Søren")]));
        var repository = new RecordingRepository
        {
            ExistingMessage = PendingActionMessage(messageId, jobId, targetId, ConversationActionType.Acknowledge)
        };
        var service = CreateService(
            repository,
            jobs,
            new TestCurrentUserContext(otherUserId, organizationId, Roles.User),
            new RecordingNotificationService());

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(0, repository.ResolveCalls);
        Assert.Null(jobs.LastStatusRequest);
    }

    [Fact]
    public async Task GetAsync_preserves_not_found_from_job_authorization_boundary()
    {
        var organizationId = Guid.NewGuid();
        var jobs = new RecordingJobService(null) { GetResult = Result<JobReportSummaryResponse>.NotFound() };
        var repository = new RecordingRepository();
        var service = CreateService(
            repository,
            jobs,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.User),
            new RecordingNotificationService());

        var result = await service.GetAsync(Guid.NewGuid(), null, null, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.ListCalls);
    }

    private static JobConversationService CreateService(
        IJobConversationRepository repository,
        IJobService jobs,
        ICurrentUserContext currentUser,
        INotificationService notifications,
        IApplicationTransactionFactory? transactionFactory = null) =>
        new(repository, jobs, currentUser, notifications, transactionFactory ?? new RecordingTransactionFactory());

    private static ConversationMessageResponse PendingActionMessage(
        Guid messageId,
        Guid jobId,
        Guid targetId,
        ConversationActionType type) =>
        new(
            messageId,
            jobId,
            Guid.NewGuid(),
            "Admin",
            type == ConversationActionType.SubmitForReview ? "Send den ind" : "Bekræft",
            [],
            new ConversationActionResponse(
                type,
                targetId,
                "Mikkel",
                ConversationActionStatus.Pending,
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
        public RecordingTransaction Transaction { get; } = new();

        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IApplicationTransaction>(Transaction);
    }

    private sealed class RecordingTransaction : IApplicationTransaction
    {
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            CommitCalls++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            RollbackCalls++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingRepository : IJobConversationRepository
    {
        public int CreateCalls { get; private set; }
        public int ListCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public string TargetDisplayName { get; init; } = "Medarbejder";
        public ConversationMessageResponse? ExistingMessage { get; set; }

        public Task<IReadOnlyList<ConversationMessageResponse>> ListAsync(Guid organizationId, Guid jobId, int limit, int offset, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<ConversationMessageResponse>>(ExistingMessage is null ? [] : [ExistingMessage]);
        }

        public Task<int> GetUnreadCountAsync(Guid organizationId, Guid jobId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<ConversationMessageResponse> CreateAsync(
            Guid organizationId,
            Guid jobId,
            Guid authorUserId,
            string body,
            IReadOnlyList<Guid> mentionedUserIds,
            ConversationActionType? actionType,
            Guid? actionTargetUserId,
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
            if (ExistingMessage?.Action is not { Status: ConversationActionStatus.Pending } action || action.TargetUserId != resolverUserId)
                return Task.FromResult(false);

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

    private sealed class RecordingJobService(JobReportSummaryResponse? summary) : IJobService
    {
        public Result<JobReportSummaryResponse>? GetResult { get; init; }
        public ChangeJobStatusRequest? LastStatusRequest { get; private set; }

        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(GetResult ?? (summary is null
                ? Result<JobReportSummaryResponse>.NotFound()
                : Result<JobReportSummaryResponse>.Success(summary)));

        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken)
        {
            LastStatusRequest = request;
            return Task.FromResult(summary is null
                ? Result<JobReportSummaryResponse>.NotFound()
                : Result<JobReportSummaryResponse>.Success(summary));
        }

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
        public List<MentionCall> Mentions { get; } = [];
        public List<ActionCall> Actions { get; } = [];
        public bool ThrowOnAction { get; init; }

        public Task QueueConversationMentionAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string actorName, Guid messageId, CancellationToken cancellationToken)
        {
            Mentions.Add(new MentionCall(userId, recipientName, jobId, messageId));
            return Task.CompletedTask;
        }

        public Task QueueConversationActionRequestedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string actorName, string actionLabel, Guid messageId, CancellationToken cancellationToken)
        {
            if (ThrowOnAction)
                throw new InvalidOperationException("notification queue failed");

            Actions.Add(new ActionCall(userId, recipientName, jobId, actionLabel, messageId));
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

    private sealed record MentionCall(Guid UserId, string RecipientName, Guid JobId, Guid MessageId);
    private sealed record ActionCall(Guid UserId, string RecipientName, Guid JobId, string ActionLabel, Guid MessageId);
}
