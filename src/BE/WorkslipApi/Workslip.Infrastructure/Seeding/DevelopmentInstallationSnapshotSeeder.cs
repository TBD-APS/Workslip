using Bogus;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

internal static class DevelopmentInstallationSnapshotSeeder
{
    internal static void Stage(
        SqlDbContext context,
        IReadOnlyList<JobReportRow> jobReports,
        ProvisionedInstallationBaseline baseline,
        CancellationToken cancellationToken)
    {
        var random = new Faker();
        var selectedInstallations = new List<JobReportInstallationRow>();
        var selectedCategories = new List<JobReportInstallationCategoryRow>();
        var selectedControlPoints = new List<JobReportInstallationControlPointRow>();
        var mappingsByDefinitionId = baseline.Mappings
            .GroupBy(mapping => mapping.InstallationTypeDefinitionId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var job in jobReports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedDefinitions = baseline.Definitions
                .OrderBy(_ => random.Random.Int())
                .Take(random.Random.Int(1, Math.Min(3, baseline.Definitions.Count)))
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

                var categorySortOrder = 1;
                foreach (var categoryGroup in mappingsForDefinition
                    .GroupBy(mapping => mapping.ControlCategoryId))
                {
                    var selectedCategory = new JobReportInstallationCategoryRow
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = job.OrganizationId,
                        JobReportInstallationId = selectedInstallation.Id,
                        ControlCategoryId = categoryGroup.Key,
                        SortOrder = categorySortOrder++,
                        IsIrrelevant = random.Random.Bool(0.8f)
                    };
                    selectedCategories.Add(selectedCategory);

                    foreach (var mapping in categoryGroup.OrderBy(mapping => mapping.SortOrder))
                    {
                        var isRelevant = !selectedCategory.IsIrrelevant;
                        selectedControlPoints.Add(new JobReportInstallationControlPointRow
                        {
                            OrganizationId = job.OrganizationId,
                            JobReportInstallationCategoryId = selectedCategory.Id,
                            ControlPointId = mapping.ControlPointId,
                            SortOrder = mapping.SortOrder,
                            IsRequired = mapping.IsRequired,
                            IsChecked = isRelevant && random.Random.Bool(0.25f)
                        });
                    }
                }
            }
        }

        context.JobReportInstallations.AddRange(selectedInstallations);
        context.JobReportInstallationCategories.AddRange(selectedCategories);
        context.JobReportInstallationControlPoints.AddRange(selectedControlPoints);
    }
}
