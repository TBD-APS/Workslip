using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Inventory;
using Xunit;

namespace Workslip.Tests.Inventory;

public sealed class InventoryServiceTests
{
    [Fact]
    public void Qr_payload_roundtrips_only_for_workslip_inventory_codes()
    {
        var code = Guid.NewGuid();
        var payload = InventoryService.BuildQrPayload(code);

        Assert.True(InventoryService.TryParseQrCode(payload, out var parsed));
        Assert.Equal(code, parsed);
        Assert.False(InventoryService.TryParseQrCode(code.ToString(), out _));
        Assert.False(InventoryService.TryParseQrCode("https://example.com/qr", out _));
    }

    [Fact]
    public async Task Scan_uses_authenticated_organization_and_never_accepts_foreign_material()
    {
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var qr = Guid.NewGuid();
        var repository = new FakeRepository
        {
            MaterialOrganizationId = foreignOrganizationId,
            Material = new InventoryMaterialResponse(Guid.NewGuid(), "Kobberrør", "KR-15", "m", 12.5m, true, qr)
        };
        var service = new InventoryService(repository, new TestUser(Guid.NewGuid(), organizationId));

        var result = await service.ScanAsync(new ScanInventoryRequest(InventoryService.BuildQrPayload(qr)), default);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(organizationId, repository.LastOrganizationId);
        Assert.Equal(qr, repository.LastQrCode);
    }

    [Fact]
    public async Task Scan_returns_all_active_location_balances_for_material()
    {
        var organizationId = Guid.NewGuid();
        var materialId = Guid.NewGuid();
        var qr = Guid.NewGuid();
        var repository = new FakeRepository
        {
            MaterialOrganizationId = organizationId,
            Material = new InventoryMaterialResponse(materialId, "Kobberrør", "KR-15", "m", 12.5m, true, qr),
            Balances =
            [
                new InventoryBalanceResponse(materialId, Guid.NewGuid(), "Bil 1", 8m),
                new InventoryBalanceResponse(materialId, Guid.NewGuid(), "Hovedlager", 22m)
            ]
        };
        var service = new InventoryService(repository, new TestUser(Guid.NewGuid(), organizationId));

        var result = await service.ScanAsync(new ScanInventoryRequest(InventoryService.BuildQrPayload(qr)), default);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal("KR-15", result.Value.Sku);
        Assert.Equal(2, result.Value.Balances.Count);
    }

    [Fact]
    public async Task Insufficient_stock_is_a_conflict_with_actionable_message()
    {
        var organizationId = Guid.NewGuid();
        var repository = new FakeRepository { ApplyStatus = InventoryApplyStatus.InsufficientStock };
        var service = new InventoryService(repository, new TestUser(Guid.NewGuid(), organizationId));

        var result = await service.ApplyMovementAsync(new ApplyInventoryMovementRequest(
            Guid.NewGuid(), Guid.NewGuid(), "out", 4, Guid.NewGuid(), null), default);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("ikke nok på lager", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retry_response_is_marked_as_replay_without_changing_command_identity()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var movement = new InventoryMovementResponse(
            Guid.NewGuid(), Guid.NewGuid(), "Kobberrør", Guid.NewGuid(), "Bil 1", "out", -2m, 6m,
            commandId, userId, "Niels", null, DateTimeOffset.UtcNow);
        var repository = new FakeRepository
        {
            ApplyStatus = InventoryApplyStatus.Replay,
            AppliedMovement = movement
        };
        var service = new InventoryService(repository, new TestUser(userId, organizationId));

        var result = await service.ApplyMovementAsync(new ApplyInventoryMovementRequest(
            movement.MaterialId, movement.LocationId, "out", 2m, commandId, null), default);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.True(result.Value.IsReplay);
        Assert.Equal(commandId, result.Value.CommandId);
    }

    [Fact]
    public async Task Movement_validation_rejects_zero_quantity_and_missing_command_id_before_repository()
    {
        var repository = new FakeRepository();
        var service = new InventoryService(repository, new TestUser(Guid.NewGuid(), Guid.NewGuid()));

        var result = await service.ApplyMovementAsync(new ApplyInventoryMovementRequest(
            Guid.NewGuid(), Guid.NewGuid(), "out", 0m, Guid.Empty, null), default);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Identifier == "Quantity");
        Assert.Contains(result.ValidationErrors, error => error.Identifier == "CommandId");
        Assert.Equal(0, repository.ApplyCalls);
    }

    private sealed class TestUser(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public Guid? OrganizationId => organizationId;
        public string? Role => "User";
    }

    private sealed class FakeRepository : IInventoryRepository
    {
        public Guid? LastOrganizationId { get; private set; }
        public Guid? LastQrCode { get; private set; }
        public Guid? MaterialOrganizationId { get; init; }
        public InventoryMaterialResponse? Material { get; init; }
        public IReadOnlyList<InventoryBalanceResponse> Balances { get; init; } = [];
        public InventoryApplyStatus ApplyStatus { get; init; } = InventoryApplyStatus.Applied;
        public InventoryMovementResponse? AppliedMovement { get; init; }
        public int ApplyCalls { get; private set; }

        public Task<IReadOnlyList<InventoryMaterialResponse>> ListMaterialsAsync(Guid organizationId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InventoryMaterialResponse>>(Material is null ? [] : [Material]);

        public Task<InventoryMaterialResponse> CreateMaterialAsync(Guid organizationId, CreateInventoryMaterialRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new InventoryMaterialResponse(Guid.NewGuid(), request.Name, request.Sku, request.Unit, request.UnitCost, true, Guid.NewGuid()));

        public Task<IReadOnlyList<InventoryLocationResponse>> ListLocationsAsync(Guid organizationId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InventoryLocationResponse>>([]);

        public Task<InventoryLocationResponse> CreateLocationAsync(Guid organizationId, CreateInventoryLocationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new InventoryLocationResponse(Guid.NewGuid(), request.Name, true));

        public Task<InventoryMaterialResponse?> GetMaterialByIdAsync(Guid organizationId, Guid materialId, CancellationToken cancellationToken)
            => Task.FromResult(Material is not null && MaterialOrganizationId == organizationId && Material.Id == materialId ? Material : null);

        public Task<InventoryMaterialResponse?> GetMaterialByQrCodeAsync(Guid organizationId, Guid qrCode, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            LastQrCode = qrCode;
            return Task.FromResult(Material is not null && MaterialOrganizationId == organizationId && Material.QrCode == qrCode ? Material : null);
        }

        public Task<IReadOnlyList<InventoryBalanceResponse>> GetBalancesAsync(Guid organizationId, Guid materialId, CancellationToken cancellationToken)
            => Task.FromResult(Balances);

        public Task<InventoryApplyResult> ApplyMovementAsync(Guid organizationId, Guid actorUserId, ApplyInventoryMovementRequest request, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            var movement = AppliedMovement ?? new InventoryMovementResponse(
                Guid.NewGuid(), request.MaterialId, "Vare", request.LocationId, "Lager", request.Direction,
                request.Direction == "out" ? -request.Quantity : request.Quantity,
                0m, request.CommandId, actorUserId, "Tester", request.Reason, DateTimeOffset.UtcNow);
            return Task.FromResult(new InventoryApplyResult(ApplyStatus, movement));
        }

        public Task<IReadOnlyList<InventoryMovementResponse>> ListMovementsAsync(Guid organizationId, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InventoryMovementResponse>>([]);
    }
}
