using AutoBogus;
using Bogus;
using System.Text.Json;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema
{
    public static class InstallationSeeder
    {
        public static async Task Seed(
       SqlDbContext context,
       Guid organizationId,
       List<JobReportRow> jobReports,
       CancellationToken cancellationToken = default)
        {
            var seedFilePath = Path.Combine(
                AppContext.BaseDirectory,
                "Data.json");

            var seedData = await LoadAsync(
                seedFilePath,
                cancellationToken);

            var controlCategories = CreateControlCategories(seedData, organizationId);
            var seededControlPoints = CreateControlPoints(seedData, organizationId);
            var controlPoints = seededControlPoints.Select(x => x.Row).ToList();
            var definitions = CreateInstallationTypeDefinitions(seedData, organizationId);
            var definitionMappings = CreateDefinitionMappings(
                seedData,
                definitions,
                controlCategories,
                seededControlPoints);

            var random = new Faker();

            var selectedInstallations = new List<JobReportInstallationRow>();
            var selectedCategories = new List<JobReportInstallationCategoryRow>();
            var selectedControlPoints = new List<JobReportInstallationControlPointRow>();

            var mappingsByDefinitionId = definitionMappings
                .GroupBy(x => x.InstallationTypeDefinitionId)
                .ToDictionary(
                    x => x.Key,
                    x => x.ToArray());

            foreach (var job in jobReports)
            {
                var selectedDefinitions = definitions
                    .OrderBy(_ => random.Random.Int())
                    .Take(random.Random.Int(1, Math.Min(3, definitions.Count)))
                    .ToArray();

                var installationSortOrder = 1;

                foreach (var definition in selectedDefinitions)
                {
                    var selectedInstallation = new JobReportInstallationRow
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = job.OrganizationId,
                        JobReportId = job.Id,
                        InstallationTypeDefinitionId = definition.Id,
                        SortOrder = installationSortOrder++
                    };

                    selectedInstallations.Add(selectedInstallation);

                    if (!mappingsByDefinitionId.TryGetValue(definition.Id, out var mappingsForDefinition))
                    {
                        continue;
                    }

                    var mappingsByCategory = mappingsForDefinition
                        .GroupBy(x => x.ControlCategoryId)
                        .ToArray();

                    var categorySortOrder = 1;

                    foreach (var categoryGroup in mappingsByCategory)
                    {
                        var selectedCategory = new JobReportInstallationCategoryRow
                        {
                            Id = Guid.NewGuid(),
                            JobReportInstallationId = selectedInstallation.Id,
                            ControlCategoryId = categoryGroup.Key,
                            SortOrder = categorySortOrder++,
                            IsIrrelevant = random.Random.Bool(0.1f)
                        };

                        selectedCategories.Add(selectedCategory);

                        foreach (var mapping in categoryGroup.OrderBy(x => x.SortOrder))
                        {
                            selectedControlPoints.Add(new JobReportInstallationControlPointRow
                            {
                                JobReportInstallationCategoryId = selectedCategory.Id,
                                ControlPointId = mapping.ControlPointId,
                                SortOrder = mapping.SortOrder,
                                IsRequired = mapping.IsRequired,
                                IsChecked = random.Random.Bool(0.25f)
                            });
                        }
                    }
                }
            }

            context.ControlCategoryRow.AddRange(controlCategories);
            context.ControlPointRow.AddRange(controlPoints);
            context.InstallationTypeDefinitions.AddRange(definitions);
            context.InstallationTypeDefinitionMappings.AddRange(definitionMappings);
            context.JobReportInstallations.AddRange(selectedInstallations);
            context.JobReportInstallationCategories.AddRange(selectedCategories);
            context.JobReportInstallationControlPoints.AddRange(selectedControlPoints);
        }

        private static List<ControlCategoryRow> CreateControlCategories(
            InstallationControlPointSeedData seedData,
            Guid organizationId)
        {
            return seedData.Categories
                .OrderBy(x => x.SortOrder)
                .Select(category =>
                    new AutoFaker<ControlCategoryRow>()
                        .RuleFor(x => x.Id, f => f.Random.Guid())
                        .RuleFor(x => x.OrganizationId, _ => organizationId)
                        .RuleFor(x => x.Name, _ => category.Label)
                        .RuleFor(x => x.SortOrder, _ => category.SortOrder)
                        .RuleFor(x => x.JobReportInstallationCategories, _ => [])
                        .Generate())
                .ToList();
        }

        private static List<SeededControlPoint> CreateControlPoints(
    InstallationControlPointSeedData seedData,
    Guid organizationId)
        {
            var uniqueControlPoints = seedData.InstallationTypes
                .SelectMany(x => x.Categories)
                .SelectMany(x => x.ControlPoints)
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Key)
                .ToArray();

            return uniqueControlPoints
                .Select((controlPoint, index) =>
                {
                    var row = new AutoFaker<ControlPointRow>()
                        .RuleFor(x => x.Id, f => f.Random.Guid())
                        .RuleFor(x => x.OrganizationId, _ => organizationId)
                        .RuleFor(x => x.Name, _ => controlPoint.Label)
                        .RuleFor(x => x.IsActive, _ => true)
                        .RuleFor(x => x.SortOrder, _ => index + 1)
                        .RuleFor(x => x.JobReportInstallationControlPoints, _ => [])
                        .Generate();

                    return new SeededControlPoint(controlPoint.Key, row);
                })
                .ToList();
        }

        private static List<InstallationTypeDefinitionRow> CreateInstallationTypeDefinitions(
            InstallationControlPointSeedData seedData,
            Guid organizationId)
        {
            return seedData.InstallationTypes
                .OrderBy(x => x.SortOrder)
                .Select(installationType => new InstallationTypeDefinitionRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = installationType.Label,
                    SortOrder = installationType.SortOrder
                })
                .ToList();
        }

        private static List<InstallationTypeDefinitionMappingRow> CreateDefinitionMappings(
    InstallationControlPointSeedData seedData,
    List<InstallationTypeDefinitionRow> definitions,
    List<ControlCategoryRow> controlCategories,
    List<SeededControlPoint> seededControlPoints)
        {
            var categoriesByLabel = controlCategories.ToDictionary(
                x => x.Name,
                StringComparer.OrdinalIgnoreCase);

            var controlPointsByKey = seededControlPoints.ToDictionary(
                x => x.Key,
                x => x.Row,
                StringComparer.OrdinalIgnoreCase);

            var definitionsByLabel = definitions.ToDictionary(
                x => x.Name,
                StringComparer.OrdinalIgnoreCase);

            var categoriesSeedByKey = seedData.Categories.ToDictionary(
                x => x.Key,
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
                        throw new InvalidOperationException(
                            $"Unknown category key '{category.CategoryKey}'.");
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
                            ControlCategoryId = controlCategory.Id,
                            ControlPointId = controlPoint.Id,
                            SortOrder = controlPointSeedItem.SortOrder,
                            IsRequired = controlPointSeedItem.IsRequired
                        });
                    }
                }
            }

            return mappings;
        }

        public static async Task<InstallationControlPointSeedData> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
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
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                },
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
                .Select(x => x.Key)
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

    public sealed record SeededControlPoint(
    string Key,
    ControlPointRow Row);
}
