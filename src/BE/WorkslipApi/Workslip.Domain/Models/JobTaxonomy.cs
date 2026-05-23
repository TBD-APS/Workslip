namespace Workslip.Domain.Models;

public sealed record WorkKindDefinition(
    string Id,
    string Label,
    bool RequiresCustomWorkKind);

public sealed record ClosureFlagDefinition(
    string Id,
    string Label,
    bool IsExclusive);

public sealed record JobTaxonomySnapshot(
    IReadOnlyDictionary<string, WorkKindDefinition> WorkKinds,
    IReadOnlyDictionary<string, ClosureFlagDefinition> ClosureFlags);


