using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Invitations;
using Workslip.Application.Users;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Invitations;

public sealed class InvitationStatusServiceTests
{
    [Fact]
    public async Task ClearAsync_WhenOrganizationIsMissing_ReturnsUnauthorized()
    {
        var repository = new FakeInvitationStatusRepository(CreateInvite());
        var service = CreateService(repository, new FakeEntraService(), organizationId: null);

        var result = await service.ClearAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Equal(0, repository.RevokeCalls);
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenInviteBelongsToAnotherOrganization_ReturnsNotFound()
    {
        var invite = CreateInvite();
        var repository = new FakeInvitationStatusRepository(invite);
        var service = CreateService(repository, new FakeEntraService(), Guid.NewGuid());

        var result = await service.ClearAsync(invite.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.RevokeCalls);
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenPendingInviteOwnsEntraGuest_RevokesBeforeDeletingGuestAndStatus()
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-user";
        invite.EntraCreatedByInvite = true;
        var calls = new List<string>();
        var repository = new FakeInvitationStatusRepository(invite, calls);
        var entraService = new FakeEntraService(calls);
        var service = CreateService(repository, entraService, invite.OrganizationId);

        var result = await service.ClearAsync(invite.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(new[] { "invite-revoke", "entra-delete", "invite-delete" }, calls);
        Assert.Equal(1, repository.RevokeCalls);
        Assert.Equal(1, entraService.DeleteCalls);
        Assert.Equal(1, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenInviteIsConsumed_DeletesStatusWithoutDeletingUser()
    {
        var invite = CreateInvite();
        invite.Consumed = true;
        invite.AcceptedAt = DateTimeOffset.UtcNow;
        invite.EntraUserId = "entra-user";
        invite.EntraCreatedByInvite = true;
        var repository = new FakeInvitationStatusRepository(invite);
        var entraService = new FakeEntraService();
        var service = CreateService(repository, entraService, invite.OrganizationId);

        var result = await service.ClearAsync(invite.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(0, repository.RevokeCalls);
        Assert.Equal(0, entraService.DeleteCalls);
        Assert.Equal(1, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenEnrollmentWinsRevocationRace_DoesNotDeleteEntraUser()
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-user";
        invite.EntraCreatedByInvite = true;
        var repository = new FakeInvitationStatusRepository(invite)
        {
            CompleteEnrollmentWhenRevocationIsAttempted = true
        };
        var entraService = new FakeEntraService();
        var service = CreateService(repository, entraService, invite.OrganizationId);

        var result = await service.ClearAsync(invite.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.True(invite.Consumed);
        Assert.Equal(1, repository.RevokeCalls);
        Assert.Equal(0, entraService.DeleteCalls);
        Assert.Equal(1, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenGraphCleanupFails_KeepsRevokedStatusForRetry()
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-user";
        invite.EntraCreatedByInvite = true;
        var repository = new FakeInvitationStatusRepository(invite);
        var entraService = new FakeEntraService { ThrowOnDelete = true };
        var service = CreateService(repository, entraService, invite.OrganizationId);

        var result = await service.ClearAsync(invite.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Error, result.Status);
        Assert.Contains("invite_status_clear_failed", result.Errors);
        Assert.NotNull(invite.RevokedAt);
        Assert.Equal(1, repository.RevokeCalls);
        Assert.Equal(1, entraService.DeleteCalls);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.False(repository.IsDeleted);
    }

    private static InvitationStatusService CreateService(
        FakeInvitationStatusRepository repository,
        FakeEntraService entraService,
        Guid? organizationId) =>
        new(
            repository,
            entraService,
            new FakeCurrentUserContext(organizationId),
            NullLogger<InvitationStatusService>.Instance);

    private static InviteTokenRow CreateInvite() => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Email = "invitee@example.test",
        Token = "invite-token",
        Role = "User",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
    };

    private sealed class FakeInvitationStatusRepository(
        InviteTokenRow invite,
        List<string>? calls = null) : IInvitationStatusRepository
    {
        public bool CompleteEnrollmentWhenRevocationIsAttempted { get; init; }
        public int RevokeCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool IsDeleted { get; private set; }

        public Task<InviteTokenRow?> GetByIdAsync(Guid organizationId, Guid inviteId, CancellationToken cancellationToken) =>
            Task.FromResult<InviteTokenRow?>(
                !IsDeleted && invite.OrganizationId == organizationId && invite.Id == inviteId ? invite : null);

        public Task<bool> TryRevokePendingAsync(
            Guid organizationId,
            Guid inviteId,
            string currentToken,
            DateTimeOffset revokedAt,
            string replacementToken,
            CancellationToken cancellationToken)
        {
            RevokeCalls++;

            if (CompleteEnrollmentWhenRevocationIsAttempted)
            {
                invite.Consumed = true;
                invite.AcceptedAt = DateTimeOffset.UtcNow;
                return Task.FromResult(false);
            }

            if (IsDeleted
                || invite.OrganizationId != organizationId
                || invite.Id != inviteId
                || invite.Token != currentToken
                || invite.Consumed
                || invite.RevokedAt is not null)
            {
                return Task.FromResult(false);
            }

            invite.RevokedAt = revokedAt;
            invite.ExpiresAt = revokedAt;
            invite.Token = replacementToken;
            calls?.Add("invite-revoke");
            return Task.FromResult(true);
        }

        public Task<bool> TryDeleteAsync(
            Guid organizationId,
            Guid inviteId,
            string token,
            bool consumed,
            DateTimeOffset? revokedAt,
            CancellationToken cancellationToken)
        {
            if (IsDeleted
                || invite.OrganizationId != organizationId
                || invite.Id != inviteId
                || invite.Token != token
                || invite.Consumed != consumed
                || invite.RevokedAt != revokedAt)
            {
                return Task.FromResult(false);
            }

            DeleteCalls++;
            IsDeleted = true;
            calls?.Add("invite-delete");
            return Task.FromResult(true);
        }
    }

    private sealed class FakeEntraService(List<string>? calls = null) : IUserEntraService
    {
        public bool ThrowOnDelete { get; init; }
        public int DeleteCalls { get; private set; }

        public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls++;
            calls?.Add("entra-delete");
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("Graph cleanup failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserContext(Guid? organizationId) : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => organizationId;
        public string? Role => "Admin";
    }
}
