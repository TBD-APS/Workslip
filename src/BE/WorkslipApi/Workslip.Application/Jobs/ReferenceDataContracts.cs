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
    int SortOrder,
    bool IsRequired);

public sealed record WorkKindResponse(
    Guid Id,
    string NormalizedLabel,
    string Label,
    bool RequiresCustomWorkKind,
    int SortOrder);

public sealed record ClosureFlagResponse(
    Guid Id,
    string NormalizedLabel,
    string Label,
    bool IsExclusive,
    int SortOrder);
