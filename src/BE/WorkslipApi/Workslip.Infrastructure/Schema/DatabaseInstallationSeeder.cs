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
                        .RuleFor(x => x.IsChecked, f => f.Random.Bool(0.25f))
                        .RuleFor(x => x.SortOrder, f => index + 1)
                        .Generate()).ToList();

            var categoriesByName = controlCategories.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var controlPointsByName = controlPoints.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

            // Seed installation type definitions
            var definitionNames = new[] { "Gasinstallation", "Vandinstallation", "Afløbsinstallation", "Varmeinstallation" };
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
                            SortOrder = categorySortOrder++
                        };
                        selectedCategories.Add(selectedCategory);

                        foreach (var mapping in categoryGroup.OrderBy(mapping => mapping.SortOrder))
                        {
                            selectedControlPoints.Add(new JobReportInstallationControlPointRow
                            {
                                JobReportInstallationCategoryId = selectedCategory.Id,
                                ControlPointId = mapping.ControlPointId,
                                SortOrder = mapping.SortOrder,
                                IsRequired = mapping.IsRequired
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
                new ("Gasinstallation", "Forundersøgelse", "Ansøgning på gas", 1),
                new("Varmeinstallation", "Forundersøgelse", "Ansøgning på gas", 1),

                // GAS / VARME - Modtagekontrol
                new("Gasinstallation", "Modtagekontrol", "Rør og fittings", 1),
                new("Gasinstallation", "Modtagekontrol", "Armaturer", 2),
                new("Gasinstallation", "Modtagekontrol", "Kedel / VB", 3),
                new("Gasinstallation", "Modtagekontrol", "Særlige komponenter", 4),

                new("Varmeinstallation", "Modtagekontrol", "Rør og fittings", 1),
                new("Varmeinstallation", "Modtagekontrol", "Armaturer", 2),
                new("Varmeinstallation", "Modtagekontrol", "Kedel / VB", 3),
                new("Varmeinstallation", "Modtagekontrol", "Særlige komponenter", 4),

                // GAS / VARME - Udførelseskontrol
                new("Gasinstallation", "Udførelseskontrol", "Stikledning og indføring", 1),
                new("Gasinstallation", "Udførelseskontrol", "Røråbning", 2),
                new("Gasinstallation", "Udførelseskontrol", "Tilslutning til varmtvandsforsyning", 3),

                new("Varmeinstallation", "Udførelseskontrol", "Stikledning og indføring", 1),
                new("Varmeinstallation", "Udførelseskontrol", "Røråbning", 2),
                new("Varmeinstallation", "Udførelseskontrol", "Tilslutning til varmtvandsforsyning", 3),

                // GAS / VARME - Slutkontrol
                new("Gasinstallation", "Slutkontrol", "Tæthedsprøvning", 1),
                new("Gasinstallation", "Slutkontrol", "Funktionsprøvning", 2),cd 
                new("Gasinstallation", "Slutkontrol", "Sikkerhedsarmaturer", 3),
                new("Gasinstallation", "Slutkontrol", "Optælling af materialer", 4),

                new("Varmeinstallation", "Slutkontrol", "Tæthedsprøvning", 1),
                new("Varmeinstallation", "Slutkontrol", "Funktionsprøvning", 2),
                new("Varmeinstallation", "Slutkontrol", "Sikkerhedsarmaturer", 3),
                new("Varmeinstallation", "Slutkontrol", "Optælling af materialer", 4),

                // GAS / VARME - Drift & vedligehold
                new("Gasinstallation", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Gasinstallation", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Gasinstallation", "DriftVedligehold", "Ventiler og komponenter", 3),
                new("Gasinstallation", "DriftVedligehold", "Særlige komponenter", 4),

                new("Varmeinstallation", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Varmeinstallation", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Varmeinstallation", "DriftVedligehold", "Ventiler og komponenter", 3),
                new("Varmeinstallation", "DriftVedligehold", "Særlige komponenter", 4),

                // VAND - Forundersøgelse
                new("Vandinstallation", "Forundersøgelse", "Ansøgning på vand", 1),
                new("Vandinstallation", "Forundersøgelse", "Vandkvalitet", 2),

                // VAND - Modtagekontrol
                new("Vandinstallation", "Modtagekontrol", "Rør og fittings", 1),
                new("Vandinstallation", "Modtagekontrol", "Armaturer", 2),
                new("Vandinstallation", "Modtagekontrol", "VVB / veksler", 3),
                new("Vandinstallation", "Modtagekontrol", "Særlige komponenter", 4),

                // VAND - Udførelseskontrol
                new("Vandinstallation", "Udførelseskontrol", "Stikledning og indføring", 1),
                new("Vandinstallation", "Udførelseskontrol", "Fordeler omløb afsp.", 2),
                new("Vandinstallation", "Udførelseskontrol", "Samling af blandsløjfer", 3),
                new("Vandinstallation", "Udførelseskontrol", "Tilslutning til varmtvandsforsyning", 4),
                new("Vandinstallation", "Udførelseskontrol", "Fitting presset, samlet, loddet", 5),
                new("Vandinstallation", "Udførelseskontrol", "Rør i væg og lod", 6),

                // VAND - Slutkontrol
                new("Vandinstallation", "Slutkontrol", "Trykprøvning", 1),
                new("Vandinstallation", "Slutkontrol", "Afprøvning af tapsteder", 2),
                new("Vandinstallation", "Slutkontrol", "Varmtvandstemp.", 3),
                new("Vandinstallation", "Slutkontrol", "Cirkulation", 4),
                new("Vandinstallation", "Slutkontrol", "Sikkerhedsarmaturer", 5),
                new("Vandinstallation", "Slutkontrol", "Optælling af materialer", 6),

                // VAND - Drift & vedligehold
                new("Vandinstallation", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Vandinstallation", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Vandinstallation", "DriftVedligehold", "Ventiler og armaturer", 3),
                new("Vandinstallation", "DriftVedligehold", "Særlige komponenter", 4),

                // AFLØB - Forundersøgelse
                new("Afløbsinstallation", "Forundersøgelse", "Ansøgning på afløb", 1),
                new("Afløbsinstallation", "Forundersøgelse", "Fald på ledninger", 2),
                new("Afløbsinstallation", "Forundersøgelse", "Udluftninger over tag", 3),
                new("Afløbsinstallation", "Forundersøgelse", "Vakuumventiler", 4),

                // AFLØB - Modtagekontrol
                new("Afløbsinstallation", "Modtagekontrol", "Rør og fittings", 1),
                new("Afløbsinstallation", "Modtagekontrol", "Installationsgenstande", 2),
                new("Afløbsinstallation", "Modtagekontrol", "Særlige komponenter", 3),

                // AFLØB - Udførelseskontrol
                new("Afløbsinstallation", "Udførelseskontrol", "Installationsgenstande", 1),
                new("Afløbsinstallation", "Udførelseskontrol", "Fald på ledninger", 2),

                // AFLØB - Slutkontrol
                new("Afløbsinstallation", "Slutkontrol", "Tæthedsprøvning", 1),
                new("Afløbsinstallation", "Slutkontrol", "Afprøvning af installationsgenstande", 2),
                new("Afløbsinstallation", "Slutkontrol", "Optælling af materialer", 3),

                // AFLØB - Drift & vedligehold
                new("Afløbsinstallation", "DriftVedligehold", "Driftsinstruktion", 1),
                new("Afløbsinstallation", "DriftVedligehold", "Vedligeholdsinstruktion", 2),
                new("Afløbsinstallation", "DriftVedligehold", "Brugervejledning", 3),
                new("Afløbsinstallation", "DriftVedligehold", "Særlige komponenter", 4),
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
        public ControlPointTemplate(string controlPointName, string controlCategory, string controlPoint, int order)
        {
            InstallationTypeName = controlPointName;
            CategoryName = controlCategory;
            ControlPointName = controlPoint;
            Order = order;
        }
    }

}
