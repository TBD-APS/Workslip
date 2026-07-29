using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Organizations;
using Workslip.Application.Organizations.Validators;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class OrganizationServiceTests
{
    [Fact]
    public async Task UpsertAdminAsync_WhenOrganizationDoesNotExist_ReturnsNotFoundWithoutGraphCall()
    {
        var administration = new FakeOrganizationAdministrationRepository();
        var entra = new FakeEntraService();
        var service = CreateService(administration, entra);

        var result = await service.UpsertAdminAsync(
            Guid.NewGuid(),
            new UpsertOrganizationAdminRequest("admin@example.test", "Admin", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, entra.CreateCalls);
    }

    [Fact]
    public async Task UpsertAdminAsync_WhenEmailBelongsToAnotherOrganization_ReturnsConflict()
    {
        var targetOrganization = CreateOrganization();
        var administration = new FakeOrganizationAdministrationRepository
        {
            Organization = targetOrganization,
            EmailUser = CreateUser(Guid.NewGuid(), "admin@example.test", Roles.Admin)
        };
        var entra = new FakeEntraService();
        var service = CreateService(administration, entra);

        var result = await service.UpsertAdminAsync(
            targetOrganization.Id,
            new UpsertOrganizationAdminRequest("ADMIN@example.test", "Admin", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("email_in_use", result.Errors);
        Assert.Equal(0, entra.CreateCalls);
        Assert.Equal(0, administration.CreateCalls);
        Assert.Equal(0, administration.UpdateCalls);
    }

    [Fact]
    public async Task UpsertAdminAsync_WhenPlaceholderExists_UpdatesAndLinksPlaceholder()
    {
        var organization = CreateOrganization();
        var placeholder = CreateUser(organization.Id, string.Empty, Roles.Admin);
        placeholder.EntraId = string.Empty;
        var administration = new FakeOrganizationAdministrationRepository
        {
            Organization = organization,
            UnlinkedAdmin = placeholder
        };
        var entra = new FakeEntraService
        {
            CreateResult = new CreateEntraUserResult("entra-admin", "admin@example.test", "Admin", Created: false)
        };
        var service = CreateService(administration, entra);

        var result = await service.UpsertAdminAsync(
            organization.Id,
            new UpsertOrganizationAdminRequest(" Admin@Example.Test ", " New Admin ", " 12345678 "),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(placeholder.Id, result.Value.Id);
        Assert.Equal("admin@example.test", result.Value.Email);
        Assert.Equal("New Admin", result.Value.DisplayName);
        Assert.Equal("12345678", result.Value.Phone);
        Assert.Equal(Roles.Admin, result.Value.Role);
        Assert.Equal("entra-admin", placeholder.EntraId);
        Assert.Equal(0, administration.CreateCalls);
        Assert.Equal(1, administration.UpdateCalls);
    }

    [Fact]
    public async Task UpsertAdminAsync_WhenNoUserExists_CreatesAdmin()
    {
        var organization = CreateOrganization();
        var administration = new FakeOrganizationAdministrationRepository
        {
            Organization = organization
        };
        var entra = new FakeEntraService
        {
            CreateResult = new CreateEntraUserResult("entra-admin", "admin@example.test", "Admin", Created: true)
        };
        var service = CreateService(administration, entra);

        var result = await service.UpsertAdminAsync(
            organization.Id,
            new UpsertOrganizationAdminRequest("admin@example.test", "Admin", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(organization.Id, result.Value.OrganizationId);
        Assert.Equal(Roles.Admin, result.Value.Role);
        Assert.Equal(1, administration.CreateCalls);
        Assert.Equal(0, administration.UpdateCalls);
        Assert.NotNull(administration.CreatedAdmin);
        Assert.Equal("entra-admin", administration.CreatedAdmin.EntraId);
    }

    [Fact]
    public async Task UpsertAdminAsync_WhenExistingUserIsSuperAdmin_ReturnsConflictWithoutMutation()
    {
        var organization = CreateOrganization();
        var administration = new FakeOrganizationAdministrationRepository
        {
            Organization = organization,
            EmailUser = CreateUser(organization.Id, "owner@example.test", Roles.Superadmin)
        };
        var entra = new FakeEntraService();
        var service = CreateService(administration, entra);

        var result = await service.UpsertAdminAsync(
            organization.Id,
            new UpsertOrganizationAdminRequest("owner@example.test", "Owner", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("superadmin_role_protected", result.Errors);
        Assert.Equal(0, entra.CreateCalls);
        Assert.Equal(0, administration.UpdateCalls);
    }

    [Fact]
    public async Task UpsertAdminAsync_WhenPersistenceFails_DeletesNewUnreferencedEntraUser()
    {
        var organization = CreateOrganization();
        var administration = new FakeOrganizationAdministrationRepository
        {
            Organization = organization,
            ThrowOnCreate = true,
            EntraIdentityReferenced = false
        };
        var entra = new FakeEntraService
        {
            CreateResult = new CreateEntraUserResult("entra-new", "admin@example.test", "Admin", Created: true)
        };
        var service = CreateService(administration, entra);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertAdminAsync(
            organization.Id,
            new UpsertOrganizationAdminRequest("admin@example.test", "Admin", null),
            CancellationToken.None));

        Assert.Equal(1, entra.DeleteCalls);
        Assert.Equal("entra-new", entra.DeletedUserId);
    }

    [Fact]
    public async Task UpsertAdminAsync_WhenEmailIsClaimedDuringGraphCall_ReturnsConflictAndRollsBackNewEntraUser()
    {
        var organization = CreateOrganization();
        var conflictingUser = CreateUser(Guid.NewGuid(), "admin@example.test", Roles.Admin);
        var administration = new FakeOrganizationAdministrationRepository
        {
            Organization = organization,
            EntraIdentityReferenced = false
        };
        administration.EmailResults.Enqueue(null);
        administration.EmailResults.Enqueue(conflictingUser);
        var entra = new FakeEntraService
        {
            CreateResult = new CreateEntraUserResult("entra-new", "admin@example.test", "Admin", Created: true)
        };
        var service = CreateService(administration, entra);

        var result = await service.UpsertAdminAsync(
            organization.Id,
            new UpsertOrganizationAdminRequest("admin@example.test", "Admin", null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("email_in_use", result.Errors);
        Assert.Equal(1, entra.DeleteCalls);
        Assert.Equal(0, administration.CreateCalls);
        Assert.Equal(0, administration.UpdateCalls);
    }

    private static OrganizationService CreateService(
        FakeOrganizationAdministrationRepository administration,
        FakeEntraService entra) =>
        new(
            new FakeOrganizationRepository(),
            administration,
            new CreateOrganizationRequestValidator(),
            new UpsertOrganizationAdminRequestValidator(),
            entra,
            NullLogger<OrganizationService>.Instance);

    private static OrganizationRow CreateOrganization() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test organization",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static UserDataRow CreateUser(Guid organizationId, string email, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = email,
        DisplayName = "Existing user",
        Phone = string.Empty,
        EntraEmail = email,
        EntraId = "entra-existing",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeOrganizationRepository : IOrganizationRepository
    {
        public Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken) => Task.FromResult(false);

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

    private sealed class FakeOrganizationAdministrationRepository : IOrganizationAdministrationRepository
    {
        public OrganizationRow? Organization { get; init; }
        public UserDataRow? EmailUser { get; set; }
        public UserDataRow? UnlinkedAdmin { get; init; }
        public Queue<UserDataRow?> EmailResults { get; } = new();
        public bool ThrowOnCreate { get; init; }
        public bool EntraIdentityReferenced { get; init; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public UserDataRow? CreatedAdmin { get; private set; }

        public Task<OrganizationRow?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Organization?.Id == organizationId ? Organization : null);

        public Task<UserDataRow?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            if (EmailResults.Count > 0)
            {
                return Task.FromResult(EmailResults.Dequeue());
            }

            return Task.FromResult(EmailUser?.Email == normalizedEmail ? EmailUser : null);
        }

        public Task<UserDataRow?> GetUnlinkedAdminAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(UnlinkedAdmin?.OrganizationId == organizationId ? UnlinkedAdmin : null);

        public Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken) =>
            Task.FromResult(EntraIdentityReferenced);

        public Task<Guid> CreateAdminAsync(UserDataRow admin, CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("SQL create failed");
            }

            CreatedAdmin = admin;
            EmailUser = admin;
            return Task.FromResult(admin.Id);
        }

        public Task<bool> UpdateAdminAsync(UserDataRow admin, CancellationToken cancellationToken)
        {
            if (EmailUser?.Id != admin.Id && UnlinkedAdmin?.Id != admin.Id)
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
        public CreateEntraUserResult CreateResult { get; init; } =
            new("entra-created", "admin@example.test", "Admin", Created: false);
        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public string? DeletedUserId { get; private set; }

        public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct)
        {
            CreateCalls++;
            return Task.FromResult(CreateResult);
        }

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls++;
            DeletedUserId = entraUserId;
            return Task.CompletedTask;
        }
    }
}
