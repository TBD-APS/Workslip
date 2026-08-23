using Ardalis.Result;
using Workslip.Application.Auth;

namespace Workslip.Application.Inventory;

public sealed record InventoryMaterialResponse(
    Guid Id,
    string Name,
    string Sku,
    string Unit,
    decimal UnitCost,
    bool IsActive,
    Guid QrCode);

public sealed record InventoryLocationResponse(Guid Id, string Name, bool IsActive);

public sealed record InventoryBalanceResponse(
    Guid MaterialId,
    Guid LocationId,
    string LocationName,
    decimal Quantity);

public sealed record InventoryScanResponse(
    Guid MaterialId,
    string Name,
    string Sku,
    string Unit,
    IReadOnlyList<InventoryBalanceResponse> Balances);

public sealed record InventoryMovementResponse(
    Guid Id,
    Guid MaterialId,
    string MaterialName,
    Guid LocationId,
    string LocationName,
    string MovementType,
    decimal QuantityChange,
    decimal BalanceAfter,
    Guid CommandId,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string? Reason,
    DateTimeOffset CreatedAt)
{
    public bool IsReplay { get; init; }
}

public sealed record InventoryQrLabelResponse(
    Guid MaterialId,
    string Name,
    string Sku,
    string Payload);

public sealed record CreateInventoryMaterialRequest(
    string Name,
    string Sku,
    string Unit,
    decimal UnitCost);

public sealed record CreateInventoryLocationRequest(string Name);
public sealed record ScanInventoryRequest(string Code);

public sealed record ApplyInventoryMovementRequest(
    Guid MaterialId,
    Guid LocationId,
    string Direction,
    decimal Quantity,
    Guid CommandId,
    string? Reason);

public enum InventoryApplyStatus
{
    Applied,
    Replay,
    MaterialNotFound,
    LocationNotFound,
    InsufficientStock
}

public sealed record InventoryApplyResult(
    InventoryApplyStatus Status,
    InventoryMovementResponse? Movement = null);

public interface IInventoryRepository
{
    Task<IReadOnlyList<InventoryMaterialResponse>> ListMaterialsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<InventoryMaterialResponse> CreateMaterialAsync(Guid organizationId, CreateInventoryMaterialRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryLocationResponse>> ListLocationsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<InventoryLocationResponse> CreateLocationAsync(Guid organizationId, CreateInventoryLocationRequest request, CancellationToken cancellationToken);
    Task<InventoryMaterialResponse?> GetMaterialByIdAsync(Guid organizationId, Guid materialId, CancellationToken cancellationToken);
    Task<InventoryMaterialResponse?> GetMaterialByQrCodeAsync(Guid organizationId, Guid qrCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryBalanceResponse>> GetBalancesAsync(Guid organizationId, Guid materialId, CancellationToken cancellationToken);
    Task<InventoryApplyResult> ApplyMovementAsync(
        Guid organizationId,
        Guid actorUserId,
        ApplyInventoryMovementRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryMovementResponse>> ListMovementsAsync(Guid organizationId, int limit, CancellationToken cancellationToken);
}

public interface IInventoryService
{
    Task<Result<IReadOnlyList<InventoryMaterialResponse>>> ListMaterialsAsync(CancellationToken cancellationToken);
    Task<Result<InventoryMaterialResponse>> CreateMaterialAsync(CreateInventoryMaterialRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<InventoryLocationResponse>>> ListLocationsAsync(CancellationToken cancellationToken);
    Task<Result<InventoryLocationResponse>> CreateLocationAsync(CreateInventoryLocationRequest request, CancellationToken cancellationToken);
    Task<Result<InventoryScanResponse>> ScanAsync(ScanInventoryRequest request, CancellationToken cancellationToken);
    Task<Result<InventoryMovementResponse>> ApplyMovementAsync(ApplyInventoryMovementRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<InventoryMovementResponse>>> ListMovementsAsync(int limit, CancellationToken cancellationToken);
    Task<Result<InventoryQrLabelResponse>> GetQrLabelAsync(Guid materialId, CancellationToken cancellationToken);
}

public sealed class InventoryService(
    IInventoryRepository repository,
    ICurrentUserContext currentUser) : IInventoryService
{
    private const string QrPrefix = "workslip:inventory:";
    private const decimal MaxMovementQuantity = 1_000_000m;

    public async Task<Result<IReadOnlyList<InventoryMaterialResponse>>> ListMaterialsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<IReadOnlyList<InventoryMaterialResponse>>.Unauthorized();

        return Result<IReadOnlyList<InventoryMaterialResponse>>.Success(
            await repository.ListMaterialsAsync(organizationId, cancellationToken));
    }

    public async Task<Result<InventoryMaterialResponse>> CreateMaterialAsync(
        CreateInventoryMaterialRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<InventoryMaterialResponse>.Unauthorized();

        var errors = ValidateMaterial(request);
        if (errors.Count > 0)
            return Result<InventoryMaterialResponse>.Invalid(errors);

        var normalized = request with
        {
            Name = request.Name.Trim(),
            Sku = request.Sku.Trim(),
            Unit = request.Unit.Trim(),
            UnitCost = decimal.Round(request.UnitCost, 2, MidpointRounding.AwayFromZero)
        };

        try
        {
            var created = await repository.CreateMaterialAsync(organizationId, normalized, cancellationToken);
            return Result<InventoryMaterialResponse>.Created(created);
        }
        catch (InventoryDuplicateValueException exception)
        {
            return Result<InventoryMaterialResponse>.Conflict(exception.Message);
        }
    }

    public async Task<Result<IReadOnlyList<InventoryLocationResponse>>> ListLocationsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<IReadOnlyList<InventoryLocationResponse>>.Unauthorized();

        return Result<IReadOnlyList<InventoryLocationResponse>>.Success(
            await repository.ListLocationsAsync(organizationId, cancellationToken));
    }

    public async Task<Result<InventoryLocationResponse>> CreateLocationAsync(
        CreateInventoryLocationRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<InventoryLocationResponse>.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            return Result<InventoryLocationResponse>.Invalid(new ValidationError
            {
                Identifier = nameof(request.Name),
                ErrorMessage = "Lagerlokationen skal have et navn på højst 100 tegn."
            });
        }

        try
        {
            var created = await repository.CreateLocationAsync(
                organizationId,
                request with { Name = request.Name.Trim() },
                cancellationToken);
            return Result<InventoryLocationResponse>.Created(created);
        }
        catch (InventoryDuplicateValueException exception)
        {
            return Result<InventoryLocationResponse>.Conflict(exception.Message);
        }
    }

    public async Task<Result<InventoryScanResponse>> ScanAsync(
        ScanInventoryRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<InventoryScanResponse>.Unauthorized();

        if (!TryParseQrCode(request.Code, out var qrCode))
        {
            return Result<InventoryScanResponse>.Invalid(new ValidationError
            {
                Identifier = nameof(request.Code),
                ErrorMessage = "QR-koden er ikke en gyldig Workslip lagerkode."
            });
        }

        var material = await repository.GetMaterialByQrCodeAsync(organizationId, qrCode, cancellationToken);
        if (material is null || !material.IsActive)
            return Result<InventoryScanResponse>.NotFound();

        var balances = await repository.GetBalancesAsync(organizationId, material.Id, cancellationToken);
        return Result<InventoryScanResponse>.Success(new InventoryScanResponse(
            material.Id,
            material.Name,
            material.Sku,
            material.Unit,
            balances));
    }

    public async Task<Result<InventoryMovementResponse>> ApplyMovementAsync(
        ApplyInventoryMovementRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId || currentUser.UserId is not Guid actorUserId)
            return Result<InventoryMovementResponse>.Unauthorized();

        var errors = ValidateMovement(request);
        if (errors.Count > 0)
            return Result<InventoryMovementResponse>.Invalid(errors);

        var normalizedDirection = request.Direction.Trim().ToLowerInvariant();
        var normalized = request with
        {
            Direction = normalizedDirection,
            Quantity = decimal.Round(request.Quantity, 3, MidpointRounding.AwayFromZero),
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        };

        var result = await repository.ApplyMovementAsync(
            organizationId,
            actorUserId,
            normalized,
            cancellationToken);

        return result.Status switch
        {
            InventoryApplyStatus.Applied when result.Movement is not null => Result<InventoryMovementResponse>.Success(result.Movement),
            InventoryApplyStatus.Replay when result.Movement is not null => Result<InventoryMovementResponse>.Success(result.Movement with { IsReplay = true }),
            InventoryApplyStatus.MaterialNotFound => Result<InventoryMovementResponse>.NotFound(),
            InventoryApplyStatus.LocationNotFound => Result<InventoryMovementResponse>.Invalid(new ValidationError
            {
                Identifier = nameof(request.LocationId),
                ErrorMessage = "Lagerlokationen findes ikke eller er ikke aktiv."
            }),
            InventoryApplyStatus.InsufficientStock => Result<InventoryMovementResponse>.Conflict("Der er ikke nok på lager til at gennemføre udtaget."),
            _ => Result<InventoryMovementResponse>.Error("Lagerbevægelsen kunne ikke gennemføres.")
        };
    }

    public async Task<Result<IReadOnlyList<InventoryMovementResponse>>> ListMovementsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<IReadOnlyList<InventoryMovementResponse>>.Unauthorized();

        var safeLimit = Math.Clamp(limit, 1, 200);
        return Result<IReadOnlyList<InventoryMovementResponse>>.Success(
            await repository.ListMovementsAsync(organizationId, safeLimit, cancellationToken));
    }

    public async Task<Result<InventoryQrLabelResponse>> GetQrLabelAsync(
        Guid materialId,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<InventoryQrLabelResponse>.Unauthorized();

        var material = await repository.GetMaterialByIdAsync(organizationId, materialId, cancellationToken);
        if (material is null)
            return Result<InventoryQrLabelResponse>.NotFound();

        return Result<InventoryQrLabelResponse>.Success(new InventoryQrLabelResponse(
            material.Id,
            material.Name,
            material.Sku,
            BuildQrPayload(material.QrCode)));
    }

    public static string BuildQrPayload(Guid qrCode) => $"{QrPrefix}{qrCode:D}";

    public static bool TryParseQrCode(string? value, out Guid qrCode)
    {
        qrCode = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith(QrPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return Guid.TryParse(trimmed[QrPrefix.Length..], out qrCode) && qrCode != Guid.Empty;
    }

    private static List<ValidationError> ValidateMaterial(CreateInventoryMaterialRequest request)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            errors.Add(new ValidationError { Identifier = nameof(request.Name), ErrorMessage = "Varen skal have et navn på højst 120 tegn." });
        if (string.IsNullOrWhiteSpace(request.Sku) || request.Sku.Trim().Length > 64)
            errors.Add(new ValidationError { Identifier = nameof(request.Sku), ErrorMessage = "Varen skal have et varenummer på højst 64 tegn." });
        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Trim().Length > 24)
            errors.Add(new ValidationError { Identifier = nameof(request.Unit), ErrorMessage = "Enheden skal være udfyldt og højst 24 tegn." });
        if (request.UnitCost is < 0m or > 10_000_000m)
            errors.Add(new ValidationError { Identifier = nameof(request.UnitCost), ErrorMessage = "Kostprisen skal være mellem 0 og 10.000.000 kr." });
        return errors;
    }

    private static List<ValidationError> ValidateMovement(ApplyInventoryMovementRequest request)
    {
        var errors = new List<ValidationError>();
        var direction = request.Direction?.Trim().ToLowerInvariant();
        if (direction is not ("in" or "out"))
            errors.Add(new ValidationError { Identifier = nameof(request.Direction), ErrorMessage = "Retningen skal være 'in' eller 'out'." });
        if (request.MaterialId == Guid.Empty)
            errors.Add(new ValidationError { Identifier = nameof(request.MaterialId), ErrorMessage = "Vælg en vare." });
        if (request.LocationId == Guid.Empty)
            errors.Add(new ValidationError { Identifier = nameof(request.LocationId), ErrorMessage = "Vælg en lagerlokation." });
        if (request.CommandId == Guid.Empty)
            errors.Add(new ValidationError { Identifier = nameof(request.CommandId), ErrorMessage = "Lagerhandlingen mangler et command-id." });
        if (request.Quantity <= 0m || request.Quantity > MaxMovementQuantity)
            errors.Add(new ValidationError { Identifier = nameof(request.Quantity), ErrorMessage = "Antallet skal være større end 0 og højst 1.000.000." });
        if (request.Reason?.Trim().Length > 200)
            errors.Add(new ValidationError { Identifier = nameof(request.Reason), ErrorMessage = "Bemærkningen må højst være 200 tegn." });
        return errors;
    }
}

public sealed class InventoryDuplicateValueException(string message) : Exception(message);
