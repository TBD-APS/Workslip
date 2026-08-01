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
    public async Task InviteUsersAsync_PersistsAuditorRole()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var emailService = new RecordingEmailService();
        var service = CreateService(repository, emailService, organizationId);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest(["auditor@example.com"], "https://app.example", Roles.Auditor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Roles.Auditor, Assert.Single(repository.Created).Role);
        Assert.Equal("auditor@example.com", Assert.Single(emailService.InviteRecipients));
    }

    [Fact]
    public async Task InviteUsersAsync_UpdatesRoleWhenPendingInviteIsResent()
    {
        var organizationId = Guid.NewGuid();
        var existing = CreateInvite(organizationId, Roles.User);
        var repository = new RecordingInviteRepository(existing);
        var service = CreateService(repository, new RecordingEmailService(), organizationId);

        var result = await service.InviteUsersAsync(
            new InviteUsersRequest([existing.Email], "https://app.example", Roles.Auditor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(existing, Assert.Single(repository.Updated));
        Assert.Equal(Roles.Auditor, existing.Role);
        Assert.False(existing.Consumed);
    }

    [Fact]
    public async Task InviteUsersAsync_DefaultsMissingRoleToUser()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingInviteRepository();
        var service = CreateService(repository, new RecordingEmailService(), organizationId);

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
        var service = CreateService(repository, emailService, organizationId);

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
        Guid organizationId) =>
        new(
            null!,
            inviteRepository,
            null!,
            null!,
            emailService,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin),
            NullLogger<InvitationService>.Instance);

    private static InviteTokenRow CreateInvite(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = "pending@example.com",
        Token = Guid.NewGuid().ToString("N"),
        Role = role,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(2),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        Consumed = false
    };

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

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
