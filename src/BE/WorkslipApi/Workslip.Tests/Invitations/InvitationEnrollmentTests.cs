using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Invitations;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Invitations;

public sealed class InvitationEnrollmentTests
{
    [Fact]
    public async Task CompleteEnrollmentAsync_WhenSqlCreateFails_RollsBackAndDoesNotDeletePreprovisionedEntraUser()
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-1";
        invite.EntraEmail = "jane@example.test";
        invite.EntraCreatedByInvite = true;
        invite.EntraProvisionedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var users = new FakeUserRepository { ThrowOnCreate = true };
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService { CreateResult = new CreateEntraUserResult("entra-1", "jane@example.test", "Jane", Created: false) };
        var transactionFactory = new FakeTransactionFactory();
        var service = CreateService(users, invites, entra, transactionFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteEnrollmentAsync(new EntraEnrollRequest(invite.Token, "Jane", "+45"), CancellationToken.None));

        Assert.Equal(0, entra.DeleteCalls);
        Assert.Equal(0, invites.MarkConsumedCalls);
        Assert.Equal(0, transactionFactory.Transaction.CommitCalls);
        Assert.Equal(1, transactionFactory.Transaction.RollbackCalls);
    }

    [Fact]
    public async Task CompleteEnrollmentAsync_WhenInviteExpired_ReturnsConflictAndDoesNotStartTransaction()
    {
        var invite = CreateInvite();
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var entra = new FakeEntraService();
        var transactionFactory = new FakeTransactionFactory();
        var service = CreateService(new FakeUserRepository(), new FakeInviteRepository(invite), entra, transactionFactory);

        var result = await service.CompleteEnrollmentAsync(new EntraEnrollRequest(invite.Token, "Jane", null), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("invite_expired", result.Errors);
        Assert.Equal(0, entra.CreateCalls);
        Assert.Equal(0, transactionFactory.BeginCalls);
    }

    [Fact]
    public async Task CompleteEnrollmentAsync_WhenEntraUserIdMissing_ReturnsConflict()
    {
        var invite = CreateInvite();
        var users = new FakeUserRepository();
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var transactionFactory = new FakeTransactionFactory();
        var service = CreateService(users, invites, entra, transactionFactory);

        var result = await service.CompleteEnrollmentAsync(new EntraEnrollRequest(invite.Token, "Jane", null), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("entra_user_not_provisioned", result.Errors);
        Assert.Equal(0, users.CreateCalls);
        Assert.Equal(0, invites.MarkConsumedCalls);
        Assert.Equal(0, transactionFactory.BeginCalls);
    }

    [Fact]
    public async Task CompleteEnrollmentAsync_WhenSuccess_CommitsMarksConsumedAndReturnsAuthUser()
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-1";
        invite.EntraEmail = "jane@example.test";
        var users = new FakeUserRepository();
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var transactionFactory = new FakeTransactionFactory();
        var service = CreateService(users, invites, entra, transactionFactory);

        var result = await service.CompleteEnrollmentAsync(new EntraEnrollRequest(invite.Token, " Jane ", " 123 "), CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(invite.OrganizationId, result.Value.OrganizationId);
        Assert.Equal(invite.Email, result.Value.Email);
        Assert.Equal("Jane", result.Value.DisplayName);
        Assert.Equal("User", result.Value.Role);
        Assert.Equal(0, entra.CreateCalls);
        Assert.Equal(1, invites.MarkConsumedCalls);
        Assert.True(invite.Consumed);
        Assert.Equal(1, transactionFactory.Transaction.CommitCalls);
        Assert.Equal(0, transactionFactory.Transaction.RollbackCalls);
        Assert.Equal(0, entra.DeleteCalls);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("role")]
    [InlineData("userKind")]
    public async Task CompleteEnrollmentAsync_WhenExistingUserDoesNotMatchInvite_ReturnsConflictWithoutConsuming(
        string mismatch)
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-1";
        invite.EntraEmail = invite.Email;
        invite.UserKind = UserKinds.Member;
        var existingUser = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            Email = invite.Email,
            DisplayName = "Existing",
            Role = Roles.User,
            UserKind = UserKinds.Member
        };

        switch (mismatch)
        {
            case "organization":
                existingUser.OrganizationId = Guid.NewGuid();
                break;
            case "role":
                existingUser.Role = Roles.Auditor;
                break;
            case "userKind":
                existingUser.UserKind = UserKinds.InternalTest;
                break;
        }

        var users = new FakeUserRepository { ExistingUser = existingUser };
        var invites = new FakeInviteRepository(invite);
        var transactionFactory = new FakeTransactionFactory();
        var service = CreateService(users, invites, new FakeEntraService(), transactionFactory);

        var result = await service.CompleteEnrollmentAsync(
            new EntraEnrollRequest(invite.Token, "Jane", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("invite_existing_user_mismatch", result.Errors);
        Assert.Equal(0, users.CreateCalls);
        Assert.Equal(0, invites.MarkConsumedCalls);
        Assert.False(invite.Consumed);
        Assert.Equal(0, transactionFactory.Transaction.CommitCalls);
        Assert.Equal(1, transactionFactory.Transaction.RollbackCalls);
    }

    [Fact]
    public async Task CompleteEnrollmentAsync_WhenExistingUserMatchesInvite_ConsumesInviteAndReturnsExistingUser()
    {
        var invite = CreateInvite();
        invite.EntraUserId = "entra-1";
        invite.EntraEmail = invite.Email;
        invite.UserKind = UserKinds.InternalTest;
        var existingUser = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            Email = invite.Email,
            DisplayName = "Existing",
            Role = Roles.User,
            UserKind = UserKinds.InternalTest
        };
        var users = new FakeUserRepository { ExistingUser = existingUser };
        var invites = new FakeInviteRepository(invite);
        var transactionFactory = new FakeTransactionFactory();
        var service = CreateService(users, invites, new FakeEntraService(), transactionFactory);

        var result = await service.CompleteEnrollmentAsync(
            new EntraEnrollRequest(invite.Token, "Ignored", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(existingUser.Id, result.Value?.UserId);
        Assert.Equal(existingUser.OrganizationId, result.Value?.OrganizationId);
        Assert.Equal(existingUser.Role, result.Value?.Role);
        Assert.Equal(0, users.CreateCalls);
        Assert.Equal(1, invites.MarkConsumedCalls);
        Assert.Equal(1, transactionFactory.Transaction.CommitCalls);
        Assert.Equal(0, transactionFactory.Transaction.RollbackCalls);
    }

    [Fact]
    public async Task MarkOpenedAsync_WhenInviteValid_EnsuresEntraGuestBeforeMarkingOpened()
    {
        var invite = CreateInvite();
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var service = CreateService(new FakeUserRepository(), invites, entra, new FakeTransactionFactory());

        var result = await service.MarkOpenedAsync(invite.Token, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, entra.EnsureInvitedCalls);
        Assert.Equal(invite.Email, entra.EnsureInvitedEmail);
        Assert.Equal("entra-1", invite.EntraUserId);
        Assert.Equal("jane@example.test", invite.EntraEmail);
        Assert.NotNull(invite.EntraProvisionedAt);
        Assert.Equal(1, invites.MarkOpenedCalls);
        Assert.Equal(0, entra.CreateCalls);
    }

    [Fact]
    public async Task MarkOpenedAsync_WhenEntraUserIdAlreadySet_DoesNotCallEnsureInvited()
    {
        // A second open of the same invite must NOT hit Graph again.
        // The cached EntraUserId is the source of truth and we never
        // want to risk creating a duplicate Entra guest on re-open.
        var invite = CreateInvite();
        invite.EntraUserId = "entra-1";
        invite.EntraEmail = "jane@example.test";
        invite.EntraProvisionedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var service = CreateService(new FakeUserRepository(), invites, entra, new FakeTransactionFactory());

        var result = await service.MarkOpenedAsync(invite.Token, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(0, entra.EnsureInvitedCalls);
        Assert.Equal("entra-1", invite.EntraUserId);
        Assert.Equal(1, invites.MarkOpenedCalls);
    }

    [Fact]
    public async Task MarkOpenedAsync_WhenInviteExpired_DoesNotEnsureEntraGuestOrMarkOpened()
    {
        var invite = CreateInvite();
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var service = CreateService(new FakeUserRepository(), invites, entra, new FakeTransactionFactory());

        var result = await service.MarkOpenedAsync(invite.Token, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("invite_expired", result.Errors);
        Assert.Equal(0, entra.EnsureInvitedCalls);
        Assert.Equal(0, invites.MarkOpenedCalls);
    }

    [Fact]
    public async Task MarkOpenedAsync_WhenInviteAlreadyConsumedAndUserExists_ReturnsSuccessWithUserExistsTrue()
    {
        // Re-opening an already-consumed invite must NOT treat it as
        // expired / invalid — the user completed it before. The FE
        // uses userExists=true to route them straight to login.
        var invite = CreateInvite();
        invite.Consumed = true;
        var existingUser = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            Email = invite.Email,
            DisplayName = "Jane",
            Role = "User"
        };
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var service = CreateService(
            new FakeUserRepository { ExistingUser = existingUser },
            invites, entra, new FakeTransactionFactory());

        var result = await service.MarkOpenedAsync(invite.Token, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        var response = result.Value!;
        Assert.Equal(invite.Email, response.Email);
        Assert.True(response.UserExists);
        // No Graph call, no MarkOpened rewrite — short-circuit returns
        // before any of that.
        Assert.Equal(0, entra.EnsureInvitedCalls);
        Assert.Equal(0, invites.MarkOpenedCalls);
    }

    [Fact]
    public async Task MarkOpenedAsync_WhenInviteAlreadyConsumedButUserDeleted_ReturnsSuccessWithUserExistsFalse()
    {
        // Admin removed the user after they accepted the invite.
        // Re-opening the link should NOT 409 as "invite_consumed" —
        // the FE needs to surface "contact your manager" instead.
        var invite = CreateInvite();
        invite.Consumed = true;
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var service = CreateService(new FakeUserRepository(), invites, entra, new FakeTransactionFactory());

        var result = await service.MarkOpenedAsync(invite.Token, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        var response = result.Value!;
        Assert.Equal(invite.Email, response.Email);
        Assert.False(response.UserExists);
        Assert.Equal(0, entra.EnsureInvitedCalls);
        Assert.Equal(0, invites.MarkOpenedCalls);
    }

    [Fact]
    public async Task CleanupStaleEntraInvitesAsync_WhenExpiredInviteOwnsEntraUser_DeletesAndMarksCleaned()
    {
        var invite = CreateInvite();
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        invite.EntraUserId = "entra-1";
        invite.EntraCreatedByInvite = true;
        invite.EntraProvisionedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var invites = new FakeInviteRepository(invite);
        var entra = new FakeEntraService();
        var service = CreateService(new FakeUserRepository(), invites, entra, new FakeTransactionFactory());

        var cleanedCount = await service.CleanupStaleEntraInvitesAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);

        Assert.Equal(1, cleanedCount);
        Assert.Equal(1, entra.DeleteCalls);
        Assert.Equal("entra-1", entra.DeletedUserId);
        Assert.NotNull(invite.EntraCleanedAt);
        Assert.Equal(1, invites.UpdateCalls);
    }

    [Fact]
    public async Task CleanupStaleEntraInvitesAsync_WhenSqlUserExists_DoesNotDeleteEntraUser()
    {
        var invite = CreateInvite();
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        invite.EntraUserId = "entra-1";
        invite.EntraCreatedByInvite = true;
        var invites = new FakeInviteRepository(invite);
        var users = new FakeUserRepository { ExistingUser = new UserDataRow { Id = Guid.NewGuid(), Email = invite.Email } };
        var entra = new FakeEntraService();
        var service = CreateService(users, invites, entra, new FakeTransactionFactory());

        var cleanedCount = await service.CleanupStaleEntraInvitesAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);

        Assert.Equal(0, cleanedCount);
        Assert.Equal(0, entra.DeleteCalls);
        Assert.Null(invite.EntraCleanedAt);
    }

    private static InvitationService CreateService(
        FakeUserRepository users,
        FakeInviteRepository invites,
        FakeEntraService entra,
        FakeTransactionFactory transactionFactory) =>
        new(
            users,
            invites,
            entra,
            transactionFactory,
            new FakeEmailService(),
            new FakeCurrentUserContext(),
            NullLogger<InvitationService>.Instance);

    private static InviteTokenRow CreateInvite() =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Email = "jane@example.test",
            Token = "invite-token",
            Role = "User",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Consumed = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private sealed class FakeUserRepository : IUserRepository
    {
        public bool ThrowOnCreate { get; init; }
        public int CreateCalls { get; private set; }
        public UserDataRow? ExistingUser { get; init; }

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<UserDataRow?>(null);
        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<UserDataRow?>(null);
        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult(ExistingUser);
        public Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken) => Task.FromResult<UserDataRow?>(null);
        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UserDataRow>>(Array.Empty<UserDataRow>());
        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("SQL create failed");
            }

            return Task.FromResult(user.Id);
        }

        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssignedJobResponse>>(Array.Empty<AssignedJobResponse>());
        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<decimal?>(0);
        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, UserPeriodHours>>(new Dictionary<Guid, UserPeriodHours>());
    }

    private sealed class FakeInviteRepository(InviteTokenRow invite) : IInviteRepository
    {
        public int MarkConsumedCalls { get; private set; }
        public int MarkOpenedCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }
        public Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken) => Task.FromResult(token == invite.Token ? invite : null);
        public Task<InviteTokenRow?> GetInviteByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) => Task.FromResult<InviteTokenRow?>(invite);
        public Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(new List<InviteTokenRow> { invite });
        public Task<IReadOnlyList<InviteTokenRow>> GetStaleEntraProvisionedAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InviteTokenRow>>(invite is { Consumed: false, EntraCreatedByInvite: true, EntraCleanedAt: null }
                && !string.IsNullOrWhiteSpace(invite.EntraUserId)
                && invite.ExpiresAt < now
                    ? new[] { invite }
                    : Array.Empty<InviteTokenRow>());

        public Task MarkConsumedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken)
        {
            MarkConsumedCalls++;
            inviteTokenRow.Consumed = true;
            inviteTokenRow.AcceptedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task MarkOpenedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken)
        {
            MarkOpenedCalls++;
            inviteTokenRow.OpenedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEntraService : IUserEntraService
    {
        public int CreateCalls { get; private set; }
        public int EnsureInvitedCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public string? DeletedUserId { get; private set; }
        public string? EnsureInvitedEmail { get; private set; }
        public bool ThrowOnCreate { get; init; }
        public CreateEntraUserResult CreateResult { get; init; } = new("entra-1", "jane@example.test", "Jane", Created: true);

        public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct)
        {
            CreateCalls++;
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("Entra create failed");
            }

            return Task.FromResult(CreateResult);
        }

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct)
        {
            EnsureInvitedCalls++;
            EnsureInvitedEmail = email;
            return Task.FromResult(CreateResult with { Created = false });
        }

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls++;
            DeletedUserId = entraUserId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransactionFactory : IApplicationTransactionFactory
    {
        public int BeginCalls { get; private set; }
        public FakeTransaction Transaction { get; } = new();

        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        {
            BeginCalls++;
            return Task.FromResult<IApplicationTransaction>(Transaction);
        }
    }

    private sealed class FakeTransaction : IApplicationTransaction
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

    private sealed class FakeOrganizationRepository : IOrganizationRepository
    {
        public Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<OrganizationOnboardingResponse?> CreateAsync(CreateOrganizationRequest request, string normalizedCvr, CancellationToken cancellationToken) => Task.FromResult<OrganizationOnboardingResponse?>(null);
        public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<CurrentUserResponse?>(null);
        public Task<OrganizationRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<OrganizationRow?>(new OrganizationRow { Id = id, Name = "Org", Cvr = "12345678" });
    }

    private sealed class FakeEmailService : IEmailService
    {
        public Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => Guid.NewGuid();
        public string? Role => "Admin";
    }
}
