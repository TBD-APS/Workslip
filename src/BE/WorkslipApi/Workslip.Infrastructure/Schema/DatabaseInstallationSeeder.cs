using AutoBogus;
using Bogus;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Text;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema
{
    public static class DatabaseInstallationSeeder
    {
        public static async Task Seed(SqlDbContext context, Guid organizationId, List<JobReportRow> jobReports)
        {
            var categoryName = new[]
            {
                "Forundersøgelse",
                "Modtagekontrol",
                "Udførelseskontrol",
                "Slutkontrol",
                "DriftVedligehold"
            };

            var controlCategories = categoryName
            .Select((name, index) =>
                new AutoFaker<ControlCategoryRow>()
                    .RuleFor(x => x.Id, f => f.Random.Guid())
                    .RuleFor(x => x.OrganizationId, f => organizationId)
                    .RuleFor(x => x.Name, f => name)
                    .RuleFor(x => x.SortOrder, f => index + 1)

                    .RuleFor(x => x.JobReportInstallationCategories, f => [])
                    .Generate())
            .ToList();

            var templates = GetControlCategories();
            
            var controlPoints = templates
            .Select(x => x.ControlPointName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) =>
                new AutoFaker<ControlPointRow>()
                        .RuleFor(x => x.Id, f => f.Random.Guid())
                        .RuleFor(x => x.OrganizationId, f => organizationId)
                        .RuleFor(x => x.Name, f => name)
                        .RuleFor(x => x.Description, f => null)
                        .RuleFor(x => x.IsActive, f => true)
                        .RuleFor(x => x.SortOrder, f => index + 1)
                        .RuleFor(x => x.JobReportInstallationControlPoints, f => [])
                        .Generate()).ToList();

            var categoriesByName = controlCategories.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var controlPointsByName = controlPoints.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

            // Seed installation type definitions
            var definitionNames = new[] { "Gas/Varme", "Vand", "Afløb"};
            var definitionsByName = new Dictionary<string, InstallationTypeDefinitionRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, index) in definitionNames.Select((n, i) => (n, i)))
            {
                var definition = new InstallationTypeDefinitionRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = name,
                    SortOrder = index + 1
                };
                context.InstallationTypeDefinitions.Add(definition);
                definitionsByName[name] = definition;
            }

            var definitionMappings = new List<InstallationTypeDefinitionMappingRow>();

            foreach (var template in templates)
            {
                if (!definitionsByName.TryGetValue(template.InstallationTypeName, out var definition))
                    continue;

                if (!categoriesByName.TryGetValue(template.CategoryName, out var category))
                    continue;

                if (!controlPointsByName.TryGetValue(template.ControlPointName, out var controlPoint))
                    continue;

                definitionMappings.Add(new InstallationTypeDefinitionMappingRow
                {
                    InstallationTypeDefinitionId = definition.Id,
                    ControlCategoryId = category.Id,
                    ControlPointId = controlPoint.Id,
                    SortOrder = template.Order,
                    IsRequired = true
                });
            }

            context.InstallationTypeDefinitionMappings.AddRange(definitionMappings);

            var random = new Faker();
            var selectedInstallations = new List<JobReportInstallationRow>();
            var selectedCategories = new List<JobReportInstallationCategoryRow>();
            var selectedControlPoints = new List<JobReportInstallationControlPointRow>();

            foreach (var job in jobReports)
            {
                var selectedDefinitionNames = definitionsByName.Keys
                    .OrderBy(_ => random.Random.Int())
                    .Take(random.Random.Int(1, 3))
                    .ToArray();

                var installationSortOrder = 1;
                foreach (var definitionName in selectedDefinitionNames)
                {
                    var definition = definitionsByName[definitionName];
                    var selectedInstallation = new JobReportInstallationRow
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = job.OrganizationId,
                        JobReportId = job.Id,
                        InstallationTypeDefinitionId = definition.Id,
                        SortOrder = installationSortOrder++
                    };
                    selectedInstallations.Add(selectedInstallation);

                    var mappingsForDefinition = definitionMappings
                        .Where(mapping => mapping.InstallationTypeDefinitionId == definition.Id)
                        .GroupBy(mapping => mapping.ControlCategoryId)
                        .ToArray();

                    var categorySortOrder = 1;
                    foreach (var categoryGroup in mappingsForDefinition)
                    {
                    var selectedCategory = new JobReportInstallationCategoryRow
                    {
                        Id = Guid.NewGuid(),
                        JobReportInstallationId = selectedInstallation.Id,
                        ControlCategoryId = categoryGroup.Key,
                        SortOrder = categorySortOrder++,
                        IsIrrelevant = random.Random.Bool(0.1f),
                    };
                        selectedCategories.Add(selectedCategory);

                        foreach (var mapping in categoryGroup.OrderBy(mapping => mapping.SortOrder))
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

            context.ControlPointRow.AddRange(controlPoints);
            context.ControlCategoryRow.AddRange(controlCategories);
            context.JobReportInstallations.AddRange(selectedInstallations);
            context.JobReportInstallationCategories.AddRange(selectedCategories);
            context.JobReportInstallationControlPoints.AddRange(selectedControlPoints);
        }
        public static List<ControlPointTemplate> GetControlCategories()
        {
            var templates = new List<ControlPointTemplate>
            {
                // GAS / VARME - Forundersøgelse
                new ("Gas/Varme", "Forundersøgelse", "Ansøgning på gas", 1),

                // GAS / VARME - Modtagekontrol
                new("Gas/Varme", "Modtagekontrol", "Rør og fittings", 1),
                new("Gas/Varme", "Modtagekontrol", "Armaturer", 2),
                new("Gas/Varme", "Modtagekontrol", "Kedel / VB", 3),
                new("Gas/Varme", "Modtagekontrol", "Særlige komponenter", 4),

                // GAS / VARME - Udførelseskontrol
                new("Gas/Varme", "Udførelseskontrol", "Stikledning og indføring", 1),
                new("Gas/Varme", "Udførelseskontrol", "Rørophæng", 2),
                new("Gas/Varme", "Udførelseskontrol", "Tilslutning til varmtvandsforsyning", 3),


                // GAS / VARME - Slutkontrol
                new("Gas/Varme", "Slutkontrol", "Tæthedsprøvning", 1),
                new("Gas/Varme", "Slutkontrol", "Funktionsprøvning", 2),
                new("Gas/Varme", "Slutkontrol", "Sikkerhedsarmaturer", 3),
                new("Gas/Varme", "Slutkontrol", "Optælling af materialer", 4),

                // GAS / VARME - Drift & vedligehold
                new("Gas/Varme", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Gas/Varme", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Gas/Varme", "DriftVedligehold", "Ventiler og komponenter", 3),
                new("Gas/Varme", "DriftVedligehold", "Særlige komponenter", 4),

                // VAND - Forundersøgelse
                new("Vand", "Forundersøgelse", "Ansøgning på vand", 1),
                new("Vand", "Forundersøgelse", "Vandkvalitet", 2),

                // VAND - Modtagekontrol
                new("Vand", "Modtagekontrol", "Rør og fittings", 1),
                new("Vand", "Modtagekontrol", "Armaturer", 2),
                new("Vand", "Modtagekontrol", "VVB / veksler", 3),
                new("Vand", "Modtagekontrol", "Særlige komponenter", 4),

                // VAND - Udførelseskontrol
                new("Vand", "Udførelseskontrol", "Stikledning og indføring", 1),
                new("Vand", "Udførelseskontrol", "Fordeler omløber fastsp.", 2),
                new("Vand", "Udførelseskontrol", "Samling af koblingsdåser", 3),
                new("Vand", "Udførelseskontrol", "Tilslutning til varmtvandsforsyning", 4),
                new("Vand", "Udførelseskontrol", "Fitting presset, samlet, loddet", 5),
                new("Vand", "Udførelseskontrol", "Rør i vater og lod", 6),

                // VAND - Slutkontrol
                new("Vand", "Slutkontrol", "Trykprøvning", 1),
                new("Vand", "Slutkontrol", "Afprøvning af tapsteder", 2),
                new("Vand", "Slutkontrol", "Varmtvandstemp.", 3),
                new("Vand", "Slutkontrol", "Cirkulation", 4),
                new("Vand", "Slutkontrol", "Sikkerhedsarmaturer", 5),
                new("Vand", "Slutkontrol", "Optælling af materialer", 6),

                // VAND - Drift & vedligehold
                new("Vand", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Vand", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Vand", "DriftVedligehold", "Ventiler og armaturer", 3),
                new("Vand", "DriftVedligehold", "Særlige komponenter", 4),

                // AFLØB - Forundersøgelse
                new("Afløb", "Forundersøgelse", "Ansøgning på afløb", 1),
                new("Afløb", "Forundersøgelse", "Fald på ledninger", 2),
                new("Afløb", "Forundersøgelse", "Udluftninger over tag", 3),
                new("Afløb", "Forundersøgelse", "Vakumventiler", 4),

                // AFLØB - Modtagekontrol
                new("Afløb", "Modtagekontrol", "Rør og fittings", 1),
                new("Afløb", "Modtagekontrol", "Installationsgenstande", 2),
                new("Afløb", "Modtagekontrol", "Særlige komponenter", 3),

                // AFLØB - Udførelseskontrol
                new("Afløb", "Udførelseskontrol", "Installationsgenstande", 1),
                new("Afløb", "Udførelseskontrol", "Fald på ledninger", 2),

                // AFLØB - Slutkontrol
                new("Afløb", "Slutkontrol", "Tæthedsprøvning", 1),
                new("Afløb", "Slutkontrol", "Afprøvning af installationsgenstande", 2),
                new("Afløb", "Slutkontrol", "Optælling af materialer", 3),

                // AFLØB - Drift & vedligehold
                new("Afløb", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Afløb", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Afløb", "DriftVedligehold", "Brugervejledning", 3),
                new("Afløb", "DriftVedligehold", "Særlige komponenter", 4),
            };

            return templates;
        }
    }

    public class ControlPointTemplate
    {
        public string InstallationTypeName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ControlPointName { get; set; } = string.Empty;
        public int Order { get; set; }
        public ControlPointTemplate(string installationTypeName, string controlCategory, string controlPoint, int order)
        {
            InstallationTypeName = installationTypeName;
            CategoryName = controlCategory;
            ControlPointName = controlPoint;
            Order = order;
        }
    }

}
