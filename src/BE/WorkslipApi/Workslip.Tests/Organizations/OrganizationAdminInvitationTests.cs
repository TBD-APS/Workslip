using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Organizations;
using Workslip.Application.Organizations.Validators;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class OrganizationAdminInvitationTests
{
    [Fact]
    public async Task ListAsync_ReturnsOrganizationsFromCrossTenantAdministrationRepository()
    {
        var first = CreateOrganization("Alpha", "12345678");
        var second = CreateOrganization("Beta", "87654321");
        var administrationRepository = new FakeAdministrationRepository
        {
            Organizations = [first, second]
        };
        var service = CreateService(administrationRepository, new FakeEntraService());

        var result = await service.ListAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Collection(
            result.Value,
            organization => Assert.Equal(first.Id, organization.Id),
            organization => Assert.Equal(second.Id, organization.Id));
    }

    [Fact]
    public async Task UpsertAdminAsync_UsesEntraAdminInvitationAndReturnsInvitationStatus()
    {
        var organization = CreateOrganization("Alpha", "12345678");
        var placeholder = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            DisplayName = "Initial administrator",
            Email = string.Empty,
            EntraId = string.Empty,
            EntraEmail = string.Empty,
            Phone = string.Empty,
            Role = Roles.Admin,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var administrationRepository = new FakeAdministrationRepository
        {
            Organization = organization,
            UnlinkedAdmin = placeholder
        };
        var entraService = new FakeEntraService
        {
            InviteResult = new CreateEntraUserResult(
                "entra-admin",
                "admin@example.test",
                "Admin User",
                Created: true)
        };
        var service = CreateService(administrationRepository, entraService);

        var result = await service.UpsertAdminAsync(
            organization.Id,
            new UpsertOrganizationAdminRequest(
                "ADMIN@example.test",
                "Admin User",
                "+45 12345678"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.EntraInvitationSent);
        Assert.Equal(Roles.Admin, result.Value.Role);
        Assert.Equal("admin@example.test", result.Value.Email);
        Assert.Equal(1, entraService.InviteAdminCalls);
        Assert.Equal(0, entraService.CreateUserCalls);
        Assert.Equal("admin@example.test", entraService.InvitedEmail);
        Assert.Equal(1, administrationRepository.UpdateCalls);
    }

    private static OrganizationService CreateService(
        FakeAdministrationRepository administrationRepository,
        FakeEntraService entraService) =>
        new(
            new FakeOrganizationRepository(),
            administrationRepository,
            new CreateOrganizationRequestValidator(),
            new UpsertOrganizationAdminRequestValidator(),
            entraService,
            NullLogger<OrganizationService>.Instance);

    private static OrganizationRow CreateOrganization(string name, string cvr) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Cvr = cvr,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeOrganizationRepository : IOrganizationRepository
    {
        public Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<OrganizationOnboardingResponse?> CreateAsync(
            CreateOrganizationRequest request,
            string normalizedCvr,
            CancellationToken cancellationToken) =>
            Task.FromResult<OrganizationOnboardingResponse?>(null);

        public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<CurrentUserResponse?>(null);

        public Task<OrganizationRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OrganizationRow?>(null);
    }

    private sealed class FakeAdministrationRepository : IOrganizationAdministrationRepository
    {
        public IReadOnlyList<OrganizationRow> Organizations { get; init; } = [];
        public OrganizationRow? Organization { get; init; }
        public UserDataRow? EmailUser { get; private set; }
        public UserDataRow? UnlinkedAdmin { get; init; }
        public int UpdateCalls { get; private set; }

        public Task<IReadOnlyList<OrganizationRow>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Organizations);

        public Task<OrganizationRow?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Organization?.Id == organizationId ? Organization : null);

        public Task<UserDataRow?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(EmailUser?.Email == normalizedEmail ? EmailUser : null);

        public Task<UserDataRow?> GetUnlinkedAdminAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(UnlinkedAdmin?.OrganizationId == organizationId ? UnlinkedAdmin : null);

        public Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken) =>
            Task.FromResult(EmailUser?.EntraId == entraUserId);

        public Task<Guid?> CreateAdminAsync(UserDataRow admin, CancellationToken cancellationToken)
        {
            EmailUser = admin;
            return Task.FromResult<Guid?>(admin.Id);
        }

        public Task<bool> UpdateAdminAsync(
            UserDataRow admin,
            string expectedEmail,
            string expectedEntraId,
            CancellationToken cancellationToken)
        {
            if (UnlinkedAdmin is null
                || UnlinkedAdmin.Id != admin.Id
                || UnlinkedAdmin.Email != expectedEmail
                || UnlinkedAdmin.EntraId != expectedEntraId)
            {
                return Task.FromResult(false);
            }

            UpdateCalls++;
            EmailUser = admin;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeEntraService : IUserEntraService
    {
        public CreateEntraUserResult InviteResult { get; init; } =
            new("entra-admin", "admin@example.test", "Admin", Created: false);
        public int InviteAdminCalls { get; private set; }
        public int CreateUserCalls { get; private set; }
        public string? InvitedEmail { get; private set; }

        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct)
        {
            CreateUserCalls++;
            return Task.FromResult(InviteResult);
        }

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct)
        {
            InviteAdminCalls++;
            InvitedEmail = email;
            return Task.FromResult(InviteResult);
        }

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            Task.FromResult(InviteResult);

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
