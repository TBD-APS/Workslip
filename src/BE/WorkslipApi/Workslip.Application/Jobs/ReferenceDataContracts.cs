using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record ReferenceDataResponse(
    IReadOnlyList<InstallationTypeDefinitionResponse> InstallationTypes,
    IReadOnlyList<WorkKindResponse> WorkKinds,
    IReadOnlyList<ClosureFlagResponse> ClosureFlags);

public sealed record InstallationTypeDefinitionResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<DefinitionCategoryResponse> Categories);

public sealed record DefinitionCategoryResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<DefinitionControlPointResponse> ControlPoints);

public sealed record DefinitionControlPointResponse(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsRequired);

public sealed record WorkKindResponse(
    string Id,
    string Label,
    bool RequiresCustomWorkKind,
    int SortOrder);

public sealed record ClosureFlagResponse(
    string Id,
    string Label,
    bool IsExclusive,
    int SortOrder);
