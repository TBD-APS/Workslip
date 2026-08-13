using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class UserBillingServiceTests
{
    [Fact]
    public async Task UpdateAsync_rounds_rate_and_persists_inside_current_organization()
    {
        var organizationId = Guid.NewGuid();
        var user = CreateUser(organizationId, Roles.User);
        var repository = new FakeUserRepository(user);
        var service = CreateService(repository, organizationId, Roles.Admin);

        var result = await service.UpdateAsync(
            user.Id,
            new UpdateBillableHourlyRateRequest(812.345m),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        Assert.Equal(812.35m, repository.StoredUser!.BillableHourlyRate);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100000.01)]
    public async Task UpdateAsync_rejects_out_of_range_rate(decimal rate)
    {
        var organizationId = Guid.NewGuid();
        var user = CreateUser(organizationId, Roles.User);
        var repository = new FakeUserRepository(user);
        var service = CreateService(repository, organizationId, Roles.Admin);

        var result = await service.UpdateAsync(
            user.Id,
            new UpdateBillableHourlyRateRequest(rate),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(repository.StoredUser!.BillableHourlyRate);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task GetAsync_does_not_cross_organization_boundary()
    {
        var currentOrganizationId = Guid.NewGuid();
        var user = CreateUser(Guid.NewGuid(), Roles.User);
        user.BillableHourlyRate = 900m;
        var repository = new FakeUserRepository(user);
        var service = CreateService(repository, currentOrganizationId, Roles.Admin);

        var result = await service.GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Admin_cannot_read_or_change_superadmin_rate()
    {
        var organizationId = Guid.NewGuid();
        var user = CreateUser(organizationId, Roles.Superadmin);
        user.BillableHourlyRate = 1000m;
        var repository = new FakeUserRepository(user);
        var service = CreateService(repository, organizationId, Roles.Admin);

        var read = await service.GetAsync(user.Id, CancellationToken.None);
        var update = await service.UpdateAsync(
            user.Id,
            new UpdateBillableHourlyRateRequest(1200m),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, read.Status);
        Assert.Equal(ResultStatus.Forbidden, update.Status);
        Assert.Equal(1000m, repository.StoredUser!.BillableHourlyRate);
        Assert.Equal(0, repository.UpdateCalls);
    }

    private static UserBillingService CreateService(
        IUserRepository repository,
        Guid organizationId,
        string role) =>
        new(
            repository,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, role),
            NullLogger<UserBillingService>.Instance);

    private static UserDataRow CreateUser(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        FilialId = Guid.NewGuid(),
        Email = $"{Guid.NewGuid():N}@example.test",
        DisplayName = "Rate Test",
        EntraId = Guid.NewGuid().ToString("N"),
        EntraEmail = $"{Guid.NewGuid():N}@example.test",
        Phone = string.Empty,
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId, string role) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = role;
    }

    private sealed class FakeUserRepository(UserDataRow? user) : IUserRepository
    {
        public UserDataRow? StoredUser { get; private set; } = user;
        public int UpdateCalls { get; private set; }

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(StoredUser?.Id == id ? StoredUser : null);

        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<UserDataRow?> GetByExternalIdentityAsync(
            string? entraId,
            IReadOnlyCollection<string> emailCandidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(
            Guid organizationId,
            int limit,
            int offset,
            string? search,
            string? sortBy,
            string? sortDirection,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserDataRow>>(Array.Empty<UserDataRow>());

        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Guid> CreateAsync(UserDataRow userToCreate, CancellationToken cancellationToken)
        {
            StoredUser = userToCreate;
            return Task.FromResult(userToCreate.Id);
        }

        public Task UpdateAsync(UserDataRow userToUpdate, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            StoredUser = userToUpdate;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedJobResponse>>(Array.Empty<AssignedJobResponse>());

        public Task<decimal?> GetTotalHoursAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<decimal?>(0m);

        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(
            Guid organizationId,
            DateOnly biweeklyStart,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserPeriodHours>>(
                new Dictionary<Guid, UserPeriodHours>());
    }
}
