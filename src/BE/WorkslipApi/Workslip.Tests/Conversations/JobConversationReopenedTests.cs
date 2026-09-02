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

public sealed class JobConversationReopenedTests
{
    [Fact]
    public async Task SendAsync_allows_submit_for_review_from_reopened()
    {
        var organizationId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var job = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")], JobStatus.Reopened);
        var repository = new RecordingRepository();
        var service = CreateService(repository, new RecordingJobService(job), new TestCurrentUserContext(authorId, organizationId, Roles.User));

        var result = await service.SendAsync(jobId, new CreateConversationMessageRequest("Klar til gennemsyn igen", [], ConversationActionType.SubmitForReview, targetId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.CreateCalls);
    }

    [Fact]
    public async Task SendAsync_rejects_submit_for_review_from_approved()
    {
        var organizationId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var job = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")], JobStatus.Approved);
        var repository = new RecordingRepository();
        var service = CreateService(repository, new RecordingJobService(job), new TestCurrentUserContext(authorId, organizationId, Roles.User));

        var result = await service.SendAsync(jobId, new CreateConversationMessageRequest("Forsøg at sende godkendt igen", [], ConversationActionType.SubmitForReview, targetId), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task SendAsync_rejects_submit_for_review_from_in_review()
    {
        var organizationId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var job = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")], JobStatus.InReview);
        var repository = new RecordingRepository();
        var service = CreateService(repository, new RecordingJobService(job), new TestCurrentUserContext(authorId, organizationId, Roles.User));

        var result = await service.SendAsync(jobId, new CreateConversationMessageRequest("Allerede til gennemsyn", [], ConversationActionType.SubmitForReview, targetId), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task ResolveAction_from_reopened_triggers_in_review_transition()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var job = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")], JobStatus.Reopened);
        var jobs = new RecordingJobService(job);
        var repository = new RecordingRepository { ExistingMessage = PendingActionMessage(messageId, jobId, targetId, ConversationActionType.SubmitForReview) };
        var service = CreateService(repository, jobs, new TestCurrentUserContext(targetId, organizationId, Roles.User));

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.InReview, jobs.LastStatusRequest?.Status);
        Assert.Equal(1, repository.ResolveCalls);
    }

    [Fact]
    public async Task ResolveAction_from_rejected_still_triggers_in_review()
    {
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var job = CreateSummary(jobId, organizationId, [new(targetId, "Mikkel")], JobStatus.Rejected);
        var jobs = new RecordingJobService(job);
        var repository = new RecordingRepository { ExistingMessage = PendingActionMessage(messageId, jobId, targetId, ConversationActionType.SubmitForReview) };
        var service = CreateService(repository, jobs, new TestCurrentUserContext(targetId, organizationId, Roles.User));

        var result = await service.ResolveActionAsync(jobId, messageId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.InReview, jobs.LastStatusRequest?.Status);
    }

    private static JobConversationService CreateService(IJobConversationRepository repository, IJobService jobs, ICurrentUserContext currentUser) =>
        new(repository, jobs, new RecordingAssignmentRepository(), currentUser, new RecordingNotificationService(), new RecordingTransactionFactory());

    private static ConversationMessageResponse PendingActionMessage(Guid messageId, Guid jobId, Guid targetId, ConversationActionType type) =>
        new(messageId, jobId, Guid.NewGuid(), "Admin", "Send den ind", [], new ConversationActionResponse(type, targetId, "Mikkel", ConversationActionStatus.Pending, null, null, null), DateTimeOffset.UtcNow);

    private static JobReportSummaryResponse CreateSummary(Guid jobId, Guid organizationId, IReadOnlyList<AssignedUserResponse> assignedUsers, JobStatus status) =>
        new(jobId, organizationId, "Test organization", "12345678", "R-1", status, null, new CustomerSnapshotResponse("Kunde", null, null, "Kundevej 1", null), "Jobvej 2", null, null, JobType.Diverse.ToString(), new JobReportSummaryWorkResponse(null, [], [], null), new JobReportSummaryObservationResponse(null, null, null), [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, assignedUsers, [], null, null, false, null);

    private sealed record TestCurrentUserContext(Guid? UserId, Guid? OrganizationId, string? Role) : ICurrentUserContext;

    private sealed class RecordingTransactionFactory : IApplicationTransactionFactory
    {
        public RecordingTransaction Transaction { get; } = new();
        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => Task.FromResult<IApplicationTransaction>(Transaction);
    }

    private sealed class RecordingTransaction : IApplicationTransaction
    {
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }
        public Task CommitAsync(CancellationToken cancellationToken) { CommitCalls++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken cancellationToken) { RollbackCalls++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingAssignmentRepository : IAssignmentRepository
    {
        public Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssignedUserResponse>>([]);
        public Task AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRepository : IJobConversationRepository
    {
        public int CreateCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public ConversationMessageResponse? ExistingMessage { get; set; }
        public Task<IReadOnlyList<ConversationMessageResponse>> ListAsync(Guid organizationId, Guid jobId, int limit, int offset, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationMessageResponse>>(ExistingMessage is null ? [] : [ExistingMessage]);
        public Task<int> GetUnreadCountAsync(Guid organizationId, Guid jobId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<ConversationMessageResponse> CreateAsync(Guid organizationId, Guid jobId, Guid authorUserId, string body, IReadOnlyList<Guid> mentionedUserIds, ConversationActionType? actionType, Guid? actionTargetUserId, CancellationToken cancellationToken)
        {
            CreateCalls++;
            ExistingMessage = new ConversationMessageResponse(Guid.NewGuid(), jobId, authorUserId, "Admin", body, mentionedUserIds, actionType is null || actionTargetUserId is null ? null : new ConversationActionResponse(actionType.Value, actionTargetUserId.Value, "Mikkel", ConversationActionStatus.Pending, null, null, null), DateTimeOffset.UtcNow);
            return Task.FromResult(ExistingMessage);
        }
        public Task<ConversationMessageResponse?> GetByIdAsync(Guid organizationId, Guid jobId, Guid messageId, CancellationToken cancellationToken) => Task.FromResult(ExistingMessage);
        public Task<bool> TryResolveActionAsync(Guid organizationId, Guid jobId, Guid messageId, Guid resolverUserId, DateTimeOffset resolvedUtc, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            if (ExistingMessage?.Action is not { Status: ConversationActionStatus.Pending } action || action.TargetUserId != resolverUserId) return Task.FromResult(false);
            ExistingMessage = ExistingMessage with { Action = action with { Status = ConversationActionStatus.Completed, ResolvedByUserId = resolverUserId, ResolvedByDisplayName = action.TargetDisplayName, ResolvedUtc = resolvedUtc } };
            return Task.FromResult(true);
        }
        public Task MarkReadAsync(Guid organizationId, Guid jobId, Guid userId, DateTimeOffset readUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingJobService(JobReportSummaryResponse? summary) : IJobService
    {
        public ChangeJobStatusRequest? LastStatusRequest { get; private set; }
        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(summary is null ? Result<JobReportSummaryResponse>.NotFound() : Result<JobReportSummaryResponse>.Success(summary));
        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken) { LastStatusRequest = request; return Task.FromResult(summary is null ? Result<JobReportSummaryResponse>.NotFound() : Result<JobReportSummaryResponse>.Success(summary)); }
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
        public Task QueueConversationMentionAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string actorName, Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueConversationActionRequestedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string actorName, string actionLabel, Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.NoContent());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => ("", "");
    }
}
