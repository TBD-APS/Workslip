using System.Text.Json;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class InstallationBaselineProvisioner(SqlDbContext context)
{
    public async Task<ProvisionedInstallationBaseline> ProvisionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var seedFilePath = Path.Combine(AppContext.BaseDirectory, "Data.json");
        var seedData = await LoadAsync(seedFilePath, cancellationToken);

        var controlCategories = CreateControlCategories(seedData, organizationId);
        var seededControlPoints = CreateControlPoints(seedData, organizationId);
        var controlPoints = seededControlPoints.Select(item => item.Row).ToList();
        var definitions = CreateInstallationTypeDefinitions(seedData, organizationId);
        var mappings = CreateDefinitionMappings(
            seedData,
            definitions,
            controlCategories,
            seededControlPoints);

        context.ControlCategoryRow.AddRange(controlCategories);
        context.ControlPointRow.AddRange(controlPoints);
        context.InstallationTypeDefinitions.AddRange(definitions);
        context.InstallationTypeDefinitionMappings.AddRange(mappings);

        return new ProvisionedInstallationBaseline(definitions, mappings);
    }

    private static List<ControlCategoryRow> CreateControlCategories(
        InstallationControlPointSeedData seedData,
        Guid organizationId) =>
        seedData.Categories
            .OrderBy(category => category.SortOrder)
            .Select(category => new ControlCategoryRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = category.Label,
                SortOrder = category.SortOrder,
                JobReportInstallationCategories = []
            })
            .ToList();

    private static List<SeededControlPoint> CreateControlPoints(
        InstallationControlPointSeedData seedData,
        Guid organizationId)
    {
        var uniqueControlPoints = seedData.InstallationTypes
            .SelectMany(installationType => installationType.Categories)
            .SelectMany(category => category.ControlPoints)
            .GroupBy(controlPoint => controlPoint.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(controlPoint => controlPoint.Key)
            .ToArray();

        return uniqueControlPoints
            .Select((controlPoint, index) => new SeededControlPoint(
                controlPoint.Key,
                new ControlPointRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = controlPoint.Label,
                    IsActive = true,
                    SortOrder = index + 1,
                    JobReportInstallationControlPoints = []
                }))
            .ToList();
    }

    private static List<InstallationTypeDefinitionRow> CreateInstallationTypeDefinitions(
        InstallationControlPointSeedData seedData,
        Guid organizationId) =>
        seedData.InstallationTypes
            .OrderBy(installationType => installationType.SortOrder)
            .Select(installationType => new InstallationTypeDefinitionRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = installationType.Label,
                SortOrder = installationType.SortOrder,
                Mappings = [],
                JobReportInstallations = []
            })
            .ToList();

    private static List<InstallationTypeDefinitionMappingRow> CreateDefinitionMappings(
        InstallationControlPointSeedData seedData,
        List<InstallationTypeDefinitionRow> definitions,
        List<ControlCategoryRow> controlCategories,
        List<SeededControlPoint> seededControlPoints)
    {
        var categoriesByLabel = controlCategories.ToDictionary(
            category => category.Name,
            StringComparer.OrdinalIgnoreCase);
        var controlPointsByKey = seededControlPoints.ToDictionary(
            controlPoint => controlPoint.Key,
            controlPoint => controlPoint.Row,
            StringComparer.OrdinalIgnoreCase);
        var definitionsByLabel = definitions.ToDictionary(
            definition => definition.Name,
            StringComparer.OrdinalIgnoreCase);
        var categoriesSeedByKey = seedData.Categories.ToDictionary(
            category => category.Key,
            StringComparer.OrdinalIgnoreCase);
        var mappings = new List<InstallationTypeDefinitionMappingRow>();

        foreach (var installationType in seedData.InstallationTypes)
        {
            if (!definitionsByLabel.TryGetValue(installationType.Label, out var definition))
            {
                throw new InvalidOperationException(
                    $"Installation type definition '{installationType.Label}' was not created.");
            }

            foreach (var category in installationType.Categories)
            {
                if (!categoriesSeedByKey.TryGetValue(category.CategoryKey, out var categoryDefinition))
                {
                    throw new InvalidOperationException($"Unknown category key '{category.CategoryKey}'.");
                }

                if (!categoriesByLabel.TryGetValue(categoryDefinition.Label, out var controlCategory))
                {
                    throw new InvalidOperationException(
                        $"Control category '{categoryDefinition.Label}' was not created.");
                }

                foreach (var controlPointSeedItem in category.ControlPoints)
                {
                    if (!controlPointsByKey.TryGetValue(controlPointSeedItem.Key, out var controlPoint))
                    {
                        throw new InvalidOperationException(
                            $"Control point key '{controlPointSeedItem.Key}' was not created.");
                    }

                    mappings.Add(new InstallationTypeDefinitionMappingRow
                    {
                        InstallationTypeDefinitionId = definition.Id,
                        InstallationTypeDefinition = definition,
                        ControlCategoryId = controlCategory.Id,
                        ControlCategory = controlCategory,
                        ControlPointId = controlPoint.Id,
                        ControlPoint = controlPoint,
                        SortOrder = controlPointSeedItem.SortOrder,
                        IsRequired = controlPointSeedItem.IsRequired
                    });
                }
            }
        }

        return mappings;
    }

    private static async Task<InstallationControlPointSeedData> LoadAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Installation control point seed file was not found: {filePath}",
                filePath);
        }

        await using var stream = File.OpenRead(filePath);
        var seedData = await JsonSerializer.DeserializeAsync<InstallationControlPointSeedData>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (seedData is null)
        {
            throw new InvalidOperationException(
                $"Could not deserialize installation control point seed file: {filePath}");
        }

        Validate(seedData);
        return seedData;
    }

    private static void Validate(InstallationControlPointSeedData seedData)
    {
        var categoryKeys = seedData.Categories
            .Select(category => category.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var installationType in seedData.InstallationTypes)
        {
            foreach (var category in installationType.Categories)
            {
                if (!categoryKeys.Contains(category.CategoryKey))
                {
                    throw new InvalidOperationException(
                        $"Unknown category key '{category.CategoryKey}' used by installation type '{installationType.Label}'.");
                }
            }
        }
    }
}

public sealed record ProvisionedInstallationBaseline(
    IReadOnlyList<InstallationTypeDefinitionRow> Definitions,
    IReadOnlyList<InstallationTypeDefinitionMappingRow> Mappings);

public sealed class InstallationControlPointSeedData
{
    public List<ControlCategorySeedItem> Categories { get; set; } = [];
    public List<InstallationTypeSeedItem> InstallationTypes { get; set; } = [];
}

public sealed class ControlCategorySeedItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class InstallationTypeSeedItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<InstallationCategorySeedItem> Categories { get; set; } = [];
}

public sealed class InstallationCategorySeedItem
{
    public string CategoryKey { get; set; } = string.Empty;
    public List<ControlPointSeedItem> ControlPoints { get; set; } = [];
}

public sealed class ControlPointSeedItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
}

public sealed record SeededControlPoint(string Key, ControlPointRow Row);
