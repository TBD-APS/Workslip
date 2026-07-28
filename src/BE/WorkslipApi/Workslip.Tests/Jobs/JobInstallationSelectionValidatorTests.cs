using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobInstallationSelectionValidatorTests
{
    [Fact]
    public void HaveNoDuplicateInstallations_rejects_duplicate_installation_definition_ids()
    {
        var installationId = Guid.NewGuid();
        var request = new[]
        {
            new CreateInstallationTypeRequest(installationId, []),
            new CreateInstallationTypeRequest(installationId, [])
        };

        var isValid = JobRequestValidationRules.HaveNoDuplicateInstallations(request);

        Assert.False(isValid);
    }

    [Fact]
    public void HaveNoDuplicateInstallations_ignores_null_installation_elements()
    {
        IReadOnlyList<CreateInstallationTypeRequest> request =
        [
            null!,
            new CreateInstallationTypeRequest(Guid.NewGuid(), [])
        ];

        var exception = Record.Exception(() => JobRequestValidationRules.HaveNoDuplicateInstallations(request));
        var isValid = JobRequestValidationRules.HaveNoDuplicateInstallations(request);

        Assert.Null(exception);
        Assert.True(isValid);
    }

    [Fact]
    public void HaveNoDuplicateCategories_rejects_duplicate_category_ids_under_same_installation()
    {
        var categoryId = Guid.NewGuid();
        var categories = new[]
        {
            new CreateInstallationTypeCategoryRequest(categoryId, []),
            new CreateInstallationTypeCategoryRequest(categoryId, [])
        };

        var isValid = JobRequestValidationRules.HaveNoDuplicateCategories(categories);

        Assert.False(isValid);
    }

    [Fact]
    public void HaveNoDuplicateCategories_ignores_null_category_elements()
    {
        IReadOnlyList<CreateInstallationTypeCategoryRequest> categories =
        [
            null!,
            new CreateInstallationTypeCategoryRequest(Guid.NewGuid(), [])
        ];

        var exception = Record.Exception(() => JobRequestValidationRules.HaveNoDuplicateCategories(categories));
        var isValid = JobRequestValidationRules.HaveNoDuplicateCategories(categories);

        Assert.Null(exception);
        Assert.True(isValid);
    }

    [Fact]
    public void HaveNoDuplicateControlPoints_rejects_duplicate_control_point_ids_under_same_category()
    {
        var controlPointId = Guid.NewGuid();
        var controlPoints = new[]
        {
            new CreateInstallationTypeControlPointRequest(controlPointId, null, null),
            new CreateInstallationTypeControlPointRequest(controlPointId, null, null)
        };

        var isValid = JobRequestValidationRules.HaveNoDuplicateControlPoints(controlPoints);

        Assert.False(isValid);
    }

    [Fact]
    public void HaveNoDuplicateControlPoints_ignores_null_control_point_elements()
    {
        IReadOnlyList<CreateInstallationTypeControlPointRequest> controlPoints =
        [
            null!,
            new CreateInstallationTypeControlPointRequest(Guid.NewGuid(), null, null)
        ];

        var exception = Record.Exception(() => JobRequestValidationRules.HaveNoDuplicateControlPoints(controlPoints));
        var isValid = JobRequestValidationRules.HaveNoDuplicateControlPoints(controlPoints);

        Assert.Null(exception);
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_rejects_unknown_installation_definition_id()
    {
        var request = new[]
        {
            new CreateInstallationTypeRequest(Guid.NewGuid(), [])
        };
        var referenceData = new ReferenceDataResponse([], [], []);

        var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

        Assert.Contains(errors, e => e.Identifier == "Work.InstallationTypes[0].Id" && e.ErrorMessage.Contains("Ukendt installationstype"));
    }

    [Fact]
    public void Validate_rejects_null_installation_selection()
    {
        IReadOnlyList<CreateInstallationTypeRequest> request = [null!];
        var referenceData = new ReferenceDataResponse([], [], []);

        var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

        Assert.Contains(errors, e => e.Identifier == "Work.InstallationTypes[0]");
    }

    [Fact]
    public void Validate_rejects_null_category_selection()
    {
        var installationId = Guid.NewGuid();
        IReadOnlyList<CreateInstallationTypeCategoryRequest> categories = [null!];
        var request = new[]
        {
            new CreateInstallationTypeRequest(installationId, categories)
        };
        var referenceData = new ReferenceDataResponse(
        [
            new InstallationTypeDefinitionResponse(installationId, "Gasinstallation", 1, [])
        ], [], []);

        var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

        Assert.Contains(errors, e => e.Identifier == "Work.InstallationTypes[0].Categories[0]");
    }

    [Fact]
    public void Validate_rejects_null_control_point_selection()
    {
        var installationId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        IReadOnlyList<CreateInstallationTypeControlPointRequest> controlPoints = [null!];
        var request = new[]
        {
            new CreateInstallationTypeRequest(installationId,
            [
                new CreateInstallationTypeCategoryRequest(categoryId, controlPoints)
            ])
        };
        var referenceData = new ReferenceDataResponse(
        [
            new InstallationTypeDefinitionResponse(installationId, "Gasinstallation", 1,
            [
                new DefinitionCategoryResponse(categoryId, "Modtagekontrol", 1, [])
            ])
        ], [], []);

        var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

        Assert.Contains(errors, e => e.Identifier == "Work.InstallationTypes[0].Categories[0].ControlPoints[0]");
    }

    [Fact]
    public void Validate_rejects_category_not_allowed_for_installation()
    {
        var installationId = Guid.NewGuid();
        var allowedCategoryId = Guid.NewGuid();
        var selectedCategoryId = Guid.NewGuid();

        var request = new[]
        {
            new CreateInstallationTypeRequest(installationId,
            [
                new CreateInstallationTypeCategoryRequest(selectedCategoryId, [])
            ])
        };

        var referenceData = new ReferenceDataResponse(
        [
            new InstallationTypeDefinitionResponse(installationId, "Gasinstallation", 1,
            [
                new DefinitionCategoryResponse(allowedCategoryId, "Modtagekontrol", 1, [])
            ])
        ], [], []);

        var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

        Assert.Contains(errors, e => e.Identifier == "Work.InstallationTypes[0].Categories[0].Id" && e.ErrorMessage.Contains("ikke tilladt"));
    }

    [Fact]
    public void Validate_rejects_category_control_point_pair_not_allowed_for_installation()
    {
        var installationId = Guid.NewGuid();
        var allowedCategoryId = Guid.NewGuid();
        var selectedCategoryId = Guid.NewGuid();
        var selectedControlPointId = Guid.NewGuid();
        var allowedControlPointId = Guid.NewGuid();

        var request = new[]
        {
            new CreateInstallationTypeRequest(installationId,
            [
                new CreateInstallationTypeCategoryRequest(selectedCategoryId,
                [
                    new CreateInstallationTypeControlPointRequest(selectedControlPointId, null, null)
                ])
            ])
        };

        var referenceData = new ReferenceDataResponse(
        [
            new InstallationTypeDefinitionResponse(installationId, "Gasinstallation", 1,
            [
                new DefinitionCategoryResponse(allowedCategoryId, "Modtagekontrol", 1,
                [
                    new DefinitionControlPointResponse(allowedControlPointId, "Rør og fittings", 1, true)
                ])
            ])
        ], [], []);

        var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

        Assert.Contains(errors, e => e.Identifier == "Work.InstallationTypes[0].Categories[0].ControlPoints[0].Id" && e.ErrorMessage.Contains("ikke tilladt"));
    }
}
