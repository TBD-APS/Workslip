using Ardalis.Result;

namespace Workslip.Application.Jobs;

public static class JobInstallationSelectionValidator
{
    public static List<ValidationError> Validate(
        IReadOnlyList<CreateInstallationTypeRequest>? selections,
        ReferenceDataResponse referenceData)
    {
        var errors = new List<ValidationError>();
        if (selections is null || selections.Count == 0)
        {
            return errors;
        }

        var definitions = referenceData.InstallationTypes.ToDictionary(x => x.Id);

        for (var installationIndex = 0; installationIndex < selections.Count; installationIndex++)
        {
            var selectedInstallation = selections[installationIndex];
            if (selectedInstallation is null)
            {
                errors.Add(new ValidationError
                {
                    Identifier = $"Work.InstallationTypes[{installationIndex}]",
                    ErrorMessage = "Der skal vælges en installationstype."
                });
                continue;
            }

            if (!definitions.TryGetValue(selectedInstallation.Id, out var definition))
            {
                errors.Add(new ValidationError
                {
                    Identifier = $"Work.InstallationTypes[{installationIndex}].Id",
                    ErrorMessage = $"Ukendt installationstype '{selectedInstallation.Id}'."
                });
                continue;
            }

            var allowedPairs = definition.Categories
                .SelectMany(category => category.ControlPoints.Select(controlPoint => (CategoryId: category.Id, ControlPointId: controlPoint.Id)))
                .ToHashSet();

            var categories = selectedInstallation.Categories ?? [];
            for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                var selectedCategory = categories[categoryIndex];
                if (selectedCategory is null)
                {
                    errors.Add(new ValidationError
                    {
                        Identifier = $"Work.InstallationTypes[{installationIndex}].Categories[{categoryIndex}]",
                        ErrorMessage = "Der skal vælges en kategori."
                    });
                    continue;
                }

                var controlPoints = selectedCategory.ControlPoints ?? [];

                if (!definition.Categories.Any(category => category.Id == selectedCategory.Id))
                {
                    errors.Add(new ValidationError
                    {
                        Identifier = $"Work.InstallationTypes[{installationIndex}].Categories[{categoryIndex}].Id",
                        ErrorMessage = $"Kategorien '{selectedCategory.Id}' er ikke tilladt for installationstypen '{selectedInstallation.Id}'."
                    });
                }

                for (var controlPointIndex = 0; controlPointIndex < controlPoints.Count; controlPointIndex++)
                {
                    var selectedControlPoint = controlPoints[controlPointIndex];
                    if (selectedControlPoint is null)
                    {
                        errors.Add(new ValidationError
                        {
                            Identifier = $"Work.InstallationTypes[{installationIndex}].Categories[{categoryIndex}].ControlPoints[{controlPointIndex}]",
                            ErrorMessage = "Der skal vælges et kontrolpunkt."
                        });
                        continue;
                    }

                    if (!allowedPairs.Contains((selectedCategory.Id, selectedControlPoint.Id)))
                    {
                        errors.Add(new ValidationError
                        {
                            Identifier = $"Work.InstallationTypes[{installationIndex}].Categories[{categoryIndex}].ControlPoints[{controlPointIndex}].Id",
                            ErrorMessage = $"Kontrolpunktet '{selectedControlPoint.Id}' er ikke tilladt for kategorien '{selectedCategory.Id}' på installationstypen '{selectedInstallation.Id}'."
                        });
                    }
                }
            }
        }

        return errors;
    }
}
