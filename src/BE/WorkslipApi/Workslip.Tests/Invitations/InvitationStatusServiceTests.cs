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
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenPendingInviteOwnsEntraGuest_DeletesGuestBeforeStatus()
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
        Assert.Equal(new[] { "entra-delete", "invite-delete" }, calls);
        Assert.Equal(1, entraService.DeleteCalls);
        Assert.Equal(1, repository.DeleteCalls);
    }

    [Fact]
    public async Task ClearAsync_WhenInviteIsConsumed_DeletesStatusWithoutDeletingUser()
    {
        var invite = CreateInvite();
        invite.Consumed = true;
        invite.EntraUserId = "entra-user";
        invite.EntraCreatedByInvite = true;
        var repository = new FakeInvitationStatusRepository(invite);
        var entraService = new FakeEntraService();
        var service = CreateService(repository, entraService, invite.OrganizationId);

        var result = await service.ClearAsync(invite.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(0, entraService.DeleteCalls);
        Assert.Equal(1, repository.DeleteCalls);
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
        public int DeleteCalls { get; private set; }

        public Task<InviteTokenRow?> GetByIdAsync(Guid organizationId, Guid inviteId, CancellationToken cancellationToken) =>
            Task.FromResult<InviteTokenRow?>(
                invite.OrganizationId == organizationId && invite.Id == inviteId ? invite : null);

        public Task DeleteAsync(InviteTokenRow inviteToDelete, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            calls?.Add("invite-delete");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEntraService(List<string>? calls = null) : IUserEntraService
    {
        public int DeleteCalls { get; private set; }

        public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls++;
            calls?.Add("entra-delete");
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
