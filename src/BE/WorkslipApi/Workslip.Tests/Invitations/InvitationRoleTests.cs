using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application;
using Workslip.Application.Auth;
using Workslip.Application.Invitations;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Invitations;

public sealed class InvitationRoleTests
{
    [Fact]
    public async Task InviteUsersAsync_PersistsAuditorRoleAndMemberAudience()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId, UserKinds.Member);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["auditor@example.com"], "https://app.example", Roles.Auditor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invite = Assert.Single(repository.Created);
        Assert.Equal(Roles.Auditor, invite.Role);
        Assert.Equal(UserKinds.Member, invite.UserKind);
        Assert.Equal("auditor@example.com", Assert.Single(emailService.InviteRecipients));
    }

    [Fact]
    public async Task InviteUsersAsync_InternalTestAdminPersistsInternalTestAudience()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var service = CreateService(
            repository,
            new RecordingEmailService(),
            organizationId,
            UserKinds.InternalTest);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["qa@example.com"], "https://app.example", Roles.User),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserKinds.InternalTest, Assert.Single(repository.Created).UserKind);
    }

    [Fact]
    public async Task InviteUsersAsync_BlocksRoleChangeUntilInvitationStatusIsCleared()
    {
        var organizationId = Guid.NewGuid();
        var existing = CreateInvite(organizationId, Roles.User, UserKinds.Member);
        var originalToken = existing.Token;
        var originalExpiresAt = existing.ExpiresAt;
        var repository = new RecordingInviteRepository(existing);
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId, UserKinds.Member);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest([existing.Email], "https://app.example", Roles.Auditor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var inviteResult = Assert.Single(result.Value.Results);
        Assert.False(inviteResult.Success);
        Assert.Equal(
            "Ryd den eksisterende invitationsstatus, før du sender en ny invitation med en anden rolle.",
            inviteResult.Error);
        Assert.Empty(repository.Updated);
        Assert.Empty(emailService.InviteRecipients);
        Assert.Equal(Roles.User, existing.Role);
        Assert.Equal(originalToken, existing.Token);
        Assert.Equal(originalExpiresAt, existing.ExpiresAt);
    }

    [Fact]
    public async Task InviteUsersAsync_BlocksAudienceChangeUntilInvitationStatusIsCleared()
    {
        var organizationId = Guid.NewGuid();
        var existing = CreateInvite(organizationId, Roles.User, UserKinds.Member);
        var repository = new RecordingInviteRepository(existing);
        var emailService = new RecordingEmailService();
        var service = CreateService(
            repository,
            emailService,
            organizationId,
            UserKinds.InternalTest);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest([existing.Email], "https://app.example", Roles.User),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var inviteResult = Assert.Single(result.Value.Results);
        Assert.False(inviteResult.Success);
        Assert.Equal(
            "Ryd den eksisterende invitationsstatus, før invitationen flyttes til en anden brugergruppe.",
            inviteResult.Error);
        Assert.Empty(repository.Updated);
        Assert.Empty(emailService.InviteRecipients);
        Assert.Equal(UserKinds.Member, existing.UserKind);
    }

    [Fact]
    public async Task InviteUsersAsync_ResendsInvitationWhenRoleAndAudienceAreUnchanged()
    {
        var organizationId = Guid.NewGuid();
        var existing = CreateInvite(organizationId, Roles.User, UserKinds.Member);
        var originalToken = existing.Token;
        var originalExpiresAt = existing.ExpiresAt;
        var repository = new RecordingInviteRepository(existing);
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId, UserKinds.Member);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest([existing.Email], "https://app.example", Roles.User),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value.Results).Success);
        Assert.Same(existing, Assert.Single(repository.Updated));
        Assert.Equal(Roles.User, existing.Role);
        Assert.Equal(UserKinds.Member, existing.UserKind);
        Assert.NotEqual(originalToken, existing.Token);
        Assert.True(existing.ExpiresAt > originalExpiresAt);
        Assert.False(existing.Consumed);
        Assert.Equal(existing.Email, Assert.Single(emailService.InviteRecipients));
    }

    [Fact]
    public async Task InviteUsersAsync_DefaultsMissingRoleToUser()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var service = CreateService(
            repository,
            new RecordingEmailService(),
            organizationId,
            UserKinds.Member);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["user@example.com"], "https://app.example", Role: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Roles.User, Assert.Single(repository.Created).Role);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Superadmin")]
    [InlineData("Owner")]
    public async Task InviteUsersAsync_RejectsRolesOutsideUserAndAuditor(string role)
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId, UserKinds.Member);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["privilege@example.com"], "https://app.example", role),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Empty(repository.Created);
        Assert.Empty(repository.Updated);
        Assert.Empty(emailService.InviteRecipients);
        Assert.Contains(result.ValidationErrors, error => error.Identifier == nameof(InviteUsersRequest.Role));
    }

    private static InvitationService CreateService(
        IInviteRepository inviteRepository,
        IEmailService emailService,
        Guid organizationId,
        string actorUserKind)
    {
        var actorId = Guid.NewGuid();
        return new InvitationService(
            new ActorUserRepository(new UserDataRow
            {
                Id = actorId,
                OrganizationId = organizationId,
                Role = Roles.Admin,
                UserKind = actorUserKind
            }),
            inviteRepository,
            null!,
            null!,
            emailService,
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin),
            NullLogger<InvitationService>.Instance);
    }

    private static InviteTokenRow CreateInvite(Guid organizationId, string role, string userKind) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = "pending@example.com",
        Token = Guid.NewGuid().ToString("N"),
        Role = role,
        UserKind = userKind,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(2),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        Consumed = false
    };

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class ActorUserRepository(UserDataRow actor) : IUserRepository
    {
        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(id == actor.Id ? actor : null);

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingInviteRepository(InviteTokenRow? existing = null) : IInviteRepository
    {
        public List<InviteTokenRow> Created { get; } = [];
        public List<InviteTokenRow> Updated { get; } = [];

        public Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
        {
            Created.Add(invite);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
        {
            Updated.Add(invite);
            return Task.CompletedTask;
        }

        public Task<InviteTokenRow?> GetInviteByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) =>
            Task.FromResult(existing is not null
                && existing.OrganizationId == organizationId
                && string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase)
                    ? existing
                    : null);

        public Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkConsumedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkOpenedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InviteTokenRow>> GetStaleEntraProvisionedAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingEmailService : IEmailService
    {
        public List<string> InviteRecipients { get; } = [];

        public Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken)
        {
            InviteRecipients.Add(toEmail);
            return Task.CompletedTask;
        }

        public Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
