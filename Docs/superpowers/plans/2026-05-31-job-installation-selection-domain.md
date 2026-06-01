# Job Installation Selection Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the confused job-owned installation type model with job-specific installation/category/control-point selections that reference installation reference data.

**Architecture:** Keep `InstallationTypeDefinitions`, `ControlCategories`, `ControlPoints`, and `InstallationTypeDefinitionMappings` as reference data. Add job-owned selection rows under `JobReport`, and map requests by reference IDs through a validation helper before persistence.

**Tech Stack:** .NET 10, EF Core 10, FluentValidation, Ardalis.Result, xUnit, EF Core InMemory for EF model tests.

---

## File Structure

**Create:**
- `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportInstallationRow.cs` — selected installation row for one job report.
- `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportInstallationCategoryRow.cs` — selected category row under a selected installation.
- `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportInstallationControlPointRow.cs` — selected control point row under a selected category.
- `src/BE/WorkslipApi/Workslip.Application/Jobs/JobInstallationSelectionValidator.cs` — pure validation for selected installation/category/control-point IDs against reference data.
- `src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj` — test project.
- `src/BE/WorkslipApi/Workslip.Tests/Jobs/JobInstallationSelectionValidatorTests.cs` — validator tests.
- `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/JobInstallationModelTests.cs` — EF model metadata tests.

**Modify:**
- `src/BE/WorkslipApi/Workslip.Application/Jobs/JobContracts.cs` — change create/update installation request to carry `Guid Id` instead of `string Name`.
- `src/BE/WorkslipApi/Workslip.Application/Jobs/Validators/JobRequestValidationRules.cs` — duplicate checks by selected IDs.
- `src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs` — validate installation selections using reference data.
- `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportRow.cs` — point navigation to selected installations.
- `src/BE/WorkslipApi/Workslip.Domain/Models/InstallationTypeDefinitionRow.cs` — add selected-installation navigation.
- `src/BE/WorkslipApi/Workslip.Domain/Models/ControlCategoryRow.cs` — add selected-category navigation.
- `src/BE/WorkslipApi/Workslip.Domain/Models/ControlPointRow.cs` — add selected-control-point navigation.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs` — configure new selection tables and stop mapping old job-owned installation tables.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs` — create/update/fetch selected installation trees by reference IDs.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseInstallationSeeder.cs` — seed demo jobs into new selected rows.

**Remove from active EF usage:**
- `InstallationTypeRow` and `InstallationControlPointRow` should no longer be referenced by `SqlDbContext`, `JobReportRow`, or repositories. Delete the files only if the implementation session has explicit approval to delete files; otherwise leave them unused and remove them in a follow-up cleanup.

**Commits:**
- Do not commit unless the user explicitly asks for commits in the implementation session.

---

### Task 1: Test Project Setup

**Files:**
- Create: `src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj`

- [ ] **Step 1: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.8" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Workslip.Application\Workslip.Application.csproj" />
    <ProjectReference Include="..\Workslip.Domain\Workslip.Domain.csproj" />
    <ProjectReference Include="..\Workslip.Infrastructure\Workslip.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Run the empty tests**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj`

Expected: PASS with zero tests discovered or a successful test assembly build.

---

### Task 2: Request Contracts And Duplicate Validation

**Files:**
- Test: `src/BE/WorkslipApi/Workslip.Tests/Jobs/JobInstallationSelectionValidatorTests.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Application/Jobs/JobContracts.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Application/Jobs/Validators/JobRequestValidationRules.cs`

- [ ] **Step 1: Write the failing contract/duplicate tests**

Create `JobInstallationSelectionValidatorTests.cs` with the first duplicate-focused tests. These compile only after the contract changes from `Name` to `Id`, so the first RED can be a compile failure.

```csharp
using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;

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
}
```

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter JobInstallationSelectionValidatorTests`

Expected: FAIL because `CreateInstallationTypeRequest` still expects `string Name` and the duplicate helper methods do not exist.

- [ ] **Step 3: Change request records to use reference IDs**

In `JobContracts.cs`, replace the create request records with:

```csharp
public sealed record CreateInstallationTypeControlPointRequest(
    Guid Id,
    int? SortOrder,
    bool? IsRequired);

public sealed record CreateInstallationTypeCategoryRequest(
    Guid Id,
    IReadOnlyList<CreateInstallationTypeControlPointRequest>? ControlPoints);

public sealed record CreateInstallationTypeRequest(
    Guid Id,
    IReadOnlyList<CreateInstallationTypeCategoryRequest>? Categories);
```

- [ ] **Step 4: Replace duplicate helper methods**

In `JobRequestValidationRules.cs`, replace the installation duplicate helper with ID-based helpers:

```csharp
namespace Workslip.Application.Jobs.Validators;

public static class JobRequestValidationRules
{
    internal static bool HaveNoDuplicates(IReadOnlyList<string>? items) =>
        items is null || items.Where(i => !string.IsNullOrWhiteSpace(i))
            .GroupBy(i => i.Trim(), StringComparer.OrdinalIgnoreCase)
            .All(g => g.Count() <= 1);

    public static bool HaveNoDuplicateInstallations(IReadOnlyList<CreateInstallationTypeRequest>? items) =>
        items is null || items.Where(i => i.Id != Guid.Empty)
            .GroupBy(i => i.Id)
            .All(g => g.Count() <= 1);

    public static bool HaveNoDuplicateCategories(IReadOnlyList<CreateInstallationTypeCategoryRequest>? items) =>
        items is null || items.Where(i => i.Id != Guid.Empty)
            .GroupBy(i => i.Id)
            .All(g => g.Count() <= 1);

    public static bool HaveNoDuplicateControlPoints(IReadOnlyList<CreateInstallationTypeControlPointRequest>? items) =>
        items is null || items.Where(i => i.Id != Guid.Empty)
            .GroupBy(i => i.Id)
            .All(g => g.Count() <= 1);
}
```

- [ ] **Step 5: Update FluentValidation rules**

In `CreateJobRequestValidator.cs` and `UpdateJobRequestValidator.cs`, replace the installation duplicate rule with:

```csharp
RuleFor(x => x.Work!.InstallationTypes)
    .Must(JobRequestValidationRules.HaveNoDuplicateInstallations)
    .WithMessage("Duplicate installation type is not allowed.");

RuleForEach(x => x.Work!.InstallationTypes)
    .ChildRules(installation =>
    {
        installation.RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Installation type id is required.");

        installation.RuleFor(x => x.Categories)
            .Must(JobRequestValidationRules.HaveNoDuplicateCategories)
            .WithMessage("Duplicate category is not allowed for an installation type.");

        installation.RuleForEach(x => x.Categories)
            .ChildRules(category =>
            {
                category.RuleFor(x => x.Id)
                    .NotEmpty().WithMessage("Category id is required.");

                category.RuleFor(x => x.ControlPoints)
                    .Must(JobRequestValidationRules.HaveNoDuplicateControlPoints)
                    .WithMessage("Duplicate control point is not allowed for a category.");

                category.RuleForEach(x => x.ControlPoints)
                    .ChildRules(controlPoint =>
                    {
                        controlPoint.RuleFor(x => x.Id)
                            .NotEmpty().WithMessage("Control point id is required.");
                    });
            });
    });
```

- [ ] **Step 6: Run tests to verify GREEN**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter JobInstallationSelectionValidatorTests`

Expected: PASS for the duplicate helper tests.

---

### Task 3: Reference Combination Validation

**Files:**
- Test: `src/BE/WorkslipApi/Workslip.Tests/Jobs/JobInstallationSelectionValidatorTests.cs`
- Create: `src/BE/WorkslipApi/Workslip.Application/Jobs/JobInstallationSelectionValidator.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs`

- [ ] **Step 1: Add failing tests for invalid reference combinations**

Append these tests to `JobInstallationSelectionValidatorTests.cs`:

```csharp
[Fact]
public void Validate_rejects_unknown_installation_definition_id()
{
    var request = new[]
    {
        new CreateInstallationTypeRequest(Guid.NewGuid(), [])
    };
    var referenceData = new ReferenceDataResponse([], [], []);

    var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

    Assert.Contains(errors, e => e.Identifier == "InstallationTypes[0].Id" && e.ErrorMessage.Contains("Unknown installation type"));
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
                new DefinitionControlPointResponse(allowedControlPointId, "Rør og fittings", null, 1, true)
            ])
        ])
    ], [], []);

    var errors = JobInstallationSelectionValidator.Validate(request, referenceData);

    Assert.Contains(errors, e => e.Identifier == "InstallationTypes[0].Categories[0].ControlPoints[0].Id" && e.ErrorMessage.Contains("not allowed"));
}
```

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter JobInstallationSelectionValidatorTests`

Expected: FAIL because `JobInstallationSelectionValidator` does not exist.

- [ ] **Step 3: Create the validation helper**

Create `JobInstallationSelectionValidator.cs`:

```csharp
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
            if (!definitions.TryGetValue(selectedInstallation.Id, out var definition))
            {
                errors.Add(new ValidationError
                {
                    Identifier = $"InstallationTypes[{installationIndex}].Id",
                    ErrorMessage = $"Unknown installation type '{selectedInstallation.Id}'."
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
                var controlPoints = selectedCategory.ControlPoints ?? [];

                if (!definition.Categories.Any(category => category.Id == selectedCategory.Id))
                {
                    errors.Add(new ValidationError
                    {
                        Identifier = $"InstallationTypes[{installationIndex}].Categories[{categoryIndex}].Id",
                        ErrorMessage = $"Category '{selectedCategory.Id}' is not allowed for installation type '{selectedInstallation.Id}'."
                    });
                }

                for (var controlPointIndex = 0; controlPointIndex < controlPoints.Count; controlPointIndex++)
                {
                    var selectedControlPoint = controlPoints[controlPointIndex];
                    if (!allowedPairs.Contains((selectedCategory.Id, selectedControlPoint.Id)))
                    {
                        errors.Add(new ValidationError
                        {
                            Identifier = $"InstallationTypes[{installationIndex}].Categories[{categoryIndex}].ControlPoints[{controlPointIndex}].Id",
                            ErrorMessage = $"Control point '{selectedControlPoint.Id}' is not allowed for category '{selectedCategory.Id}' on installation type '{selectedInstallation.Id}'."
                        });
                    }
                }
            }
        }

        return errors;
    }
}
```

- [ ] **Step 4: Inject reference data validation into JobService**

Add `IReferenceDataRepository referenceDataRepository` to the `JobService` primary constructor after `IJobTaxonomyRepository taxonomyRepository`.

In `CreateAsync`, after `ValidateDraftTaxonomyAsync` succeeds and before repository create, add:

```csharp
var installationSelectionErrors = await ValidateInstallationSelectionsAsync(
    organizationId.Value,
    request.Work?.InstallationTypes,
    cancellationToken);
if (installationSelectionErrors.Count != 0)
{
    logger.LogWarning("Job create installation selection validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
        organizationId.Value,
        ValidationFields(installationSelectionErrors));

    return Result<JobReportSummaryResponse>.Invalid(installationSelectionErrors);
}
```

In `UpdateAsync`, after `ValidateDraftTaxonomyAsync` succeeds and after `organizationId` is known, add:

```csharp
var installationSelectionErrors = await ValidateInstallationSelectionsAsync(
    organizationId.Value,
    request.Work?.InstallationTypes,
    cancellationToken);
if (installationSelectionErrors.Count != 0)
{
    logger.LogWarning("Job update installation selection validation failed. JobId: {JobId}. Fields: {ValidationFields}",
        id,
        ValidationFields(installationSelectionErrors));

    return Result<JobReportSummaryResponse>.Invalid(installationSelectionErrors);
}
```

Add this private method near the other validation methods:

```csharp
private async Task<List<ValidationError>> ValidateInstallationSelectionsAsync(
    Guid organizationId,
    IReadOnlyList<CreateInstallationTypeRequest>? installationTypes,
    CancellationToken cancellationToken)
{
    if (installationTypes is null || installationTypes.Count == 0)
    {
        return [];
    }

    var referenceData = await referenceDataRepository.GetAsync(organizationId, cancellationToken);
    return JobInstallationSelectionValidator.Validate(installationTypes, referenceData);
}
```

- [ ] **Step 5: Run tests to verify GREEN**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter JobInstallationSelectionValidatorTests`

Expected: PASS.

---

### Task 4: Domain Rows And EF Model

**Files:**
- Test: `src/BE/WorkslipApi/Workslip.Tests/Infrastructure/JobInstallationModelTests.cs`
- Create: `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportInstallationRow.cs`
- Create: `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportInstallationCategoryRow.cs`
- Create: `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportInstallationControlPointRow.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Domain/Models/JobReportRow.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Domain/Models/InstallationTypeDefinitionRow.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Domain/Models/ControlCategoryRow.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Domain/Models/ControlPointRow.cs`
- Modify: `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs`

- [ ] **Step 1: Write failing EF metadata tests**

Create `JobInstallationModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.Infrastructure;

public sealed class JobInstallationModelTests
{
    [Fact]
    public void JobReportInstallations_have_unique_job_installation_definition_index()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(JobReportInstallationRow));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(JobReportInstallationRow.OrganizationId),
                nameof(JobReportInstallationRow.JobReportId),
                nameof(JobReportInstallationRow.InstallationTypeDefinitionId)
            ]));
    }

    [Fact]
    public void Selected_control_points_use_category_and_control_point_composite_key()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(JobReportInstallationControlPointRow));

        Assert.NotNull(entity);
        Assert.Equal([
            nameof(JobReportInstallationControlPointRow.JobReportInstallationCategoryId),
            nameof(JobReportInstallationControlPointRow.ControlPointId)
        ], entity!.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter JobInstallationModelTests`

Expected: FAIL because the new domain rows do not exist.

- [ ] **Step 3: Add domain rows**

Create `JobReportInstallationRow.cs`:

```csharp
namespace Workslip.Domain.Models;

public sealed class JobReportInstallationRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid JobReportId { get; set; }
    public JobReportRow JobReport { get; set; } = null!;
    public Guid InstallationTypeDefinitionId { get; set; }
    public InstallationTypeDefinitionRow InstallationTypeDefinition { get; set; } = null!;
    public int SortOrder { get; set; }
    public ICollection<JobReportInstallationCategoryRow> Categories { get; set; } = [];
}
```

Create `JobReportInstallationCategoryRow.cs`:

```csharp
namespace Workslip.Domain.Models;

public sealed class JobReportInstallationCategoryRow
{
    public Guid Id { get; set; }
    public Guid JobReportInstallationId { get; set; }
    public JobReportInstallationRow JobReportInstallation { get; set; } = null!;
    public Guid ControlCategoryId { get; set; }
    public ControlCategoryRow ControlCategory { get; set; } = null!;
    public int SortOrder { get; set; }
    public ICollection<JobReportInstallationControlPointRow> ControlPoints { get; set; } = [];
}
```

Create `JobReportInstallationControlPointRow.cs`:

```csharp
namespace Workslip.Domain.Models;

public sealed class JobReportInstallationControlPointRow
{
    public Guid JobReportInstallationCategoryId { get; set; }
    public JobReportInstallationCategoryRow JobReportInstallationCategory { get; set; } = null!;
    public Guid ControlPointId { get; set; }
    public ControlPointRow ControlPoint { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
}
```

- [ ] **Step 4: Update navigation properties**

In `JobReportRow.cs`, replace the installation navigation with:

```csharp
public List<JobReportInstallationRow>? Installations { get; set; } = new();
```

In `InstallationTypeDefinitionRow.cs`, add:

```csharp
public ICollection<JobReportInstallationRow> JobReportInstallations { get; set; } = [];
```

In `ControlCategoryRow.cs`, add:

```csharp
public ICollection<JobReportInstallationCategoryRow> JobReportInstallationCategories { get; set; } = [];
```

In `ControlPointRow.cs`, add:

```csharp
public ICollection<JobReportInstallationControlPointRow> JobReportInstallationControlPoints { get; set; } = [];
```

- [ ] **Step 5: Configure new DbSets**

In `SqlDbContext.cs`, replace old job-owned installation DbSets with:

```csharp
public DbSet<JobReportInstallationRow> JobReportInstallations => Set<JobReportInstallationRow>();
public DbSet<JobReportInstallationCategoryRow> JobReportInstallationCategories => Set<JobReportInstallationCategoryRow>();
public DbSet<JobReportInstallationControlPointRow> JobReportInstallationControlPoints => Set<JobReportInstallationControlPointRow>();
```

Keep these reference DbSets:

```csharp
public DbSet<ControlPointRow> ControlPointRow => Set<ControlPointRow>();
public DbSet<ControlCategoryRow> ControlCategoryRow => Set<ControlCategoryRow>();
public DbSet<InstallationTypeDefinitionRow> InstallationTypeDefinitions => Set<InstallationTypeDefinitionRow>();
public DbSet<InstallationTypeDefinitionMappingRow> InstallationTypeDefinitionMappings => Set<InstallationTypeDefinitionMappingRow>();
```

- [ ] **Step 6: Replace old config calls**

In `OnModelCreating`, replace:

```csharp
ConfigureInstallationTypes(modelBuilder);
ConfigureInstallationControlPoint(modelBuilder);
```

with:

```csharp
ConfigureJobReportInstallations(modelBuilder);
ConfigureJobReportInstallationCategories(modelBuilder);
ConfigureJobReportInstallationControlPoints(modelBuilder);
```

- [ ] **Step 7: Add EF configuration methods**

Add these methods near the old installation config methods. Remove the old `ConfigureInstallationTypes` and `ConfigureInstallationControlPoint` calls from the model.

```csharp
private static void ConfigureJobReportInstallations(ModelBuilder modelBuilder)
{
    var entity = modelBuilder.Entity<JobReportInstallationRow>();

    entity.ToTable("JobReportInstallations", "dbo");
    entity.HasKey(x => x.Id);

    entity.Property(x => x.SortOrder)
        .HasDefaultValue(0);

    entity.HasOne(x => x.JobReport)
        .WithMany(x => x.Installations)
        .HasForeignKey(x => new { x.OrganizationId, x.JobReportId })
        .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(x => x.InstallationTypeDefinition)
        .WithMany(x => x.JobReportInstallations)
        .HasForeignKey(x => x.InstallationTypeDefinitionId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasIndex(x => new { x.OrganizationId, x.JobReportId, x.InstallationTypeDefinitionId })
        .IsUnique();

    entity.HasIndex(x => new { x.OrganizationId, x.JobReportId, x.SortOrder });
}

private static void ConfigureJobReportInstallationCategories(ModelBuilder modelBuilder)
{
    var entity = modelBuilder.Entity<JobReportInstallationCategoryRow>();

    entity.ToTable("JobReportInstallationCategories", "dbo");
    entity.HasKey(x => x.Id);

    entity.Property(x => x.SortOrder)
        .HasDefaultValue(0);

    entity.HasOne(x => x.JobReportInstallation)
        .WithMany(x => x.Categories)
        .HasForeignKey(x => x.JobReportInstallationId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(x => x.ControlCategory)
        .WithMany(x => x.JobReportInstallationCategories)
        .HasForeignKey(x => x.ControlCategoryId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasIndex(x => new { x.JobReportInstallationId, x.ControlCategoryId })
        .IsUnique();

    entity.HasIndex(x => new { x.JobReportInstallationId, x.SortOrder });
}

private static void ConfigureJobReportInstallationControlPoints(ModelBuilder modelBuilder)
{
    var entity = modelBuilder.Entity<JobReportInstallationControlPointRow>();

    entity.ToTable("JobReportInstallationControlPoints", "dbo");
    entity.HasKey(x => new { x.JobReportInstallationCategoryId, x.ControlPointId });

    entity.Property(x => x.SortOrder)
        .HasDefaultValue(0);

    entity.Property(x => x.IsRequired)
        .HasDefaultValue(false);

    entity.HasOne(x => x.JobReportInstallationCategory)
        .WithMany(x => x.ControlPoints)
        .HasForeignKey(x => x.JobReportInstallationCategoryId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(x => x.ControlPoint)
        .WithMany(x => x.JobReportInstallationControlPoints)
        .HasForeignKey(x => x.ControlPointId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasIndex(x => new { x.JobReportInstallationCategoryId, x.SortOrder });
}
```

- [ ] **Step 8: Run tests to verify GREEN**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj --filter JobInstallationModelTests`

Expected: PASS.

---

### Task 5: Repository Create, Update, And Fetch

**Files:**
- Modify: `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs`

- [ ] **Step 1: Run build to expose old model references**

Run: `dotnet build src/BE/WorkslipApi/Workslip.Api.csproj`

Expected: FAIL with references to `InstallationTypeRow`, `InstallationControlPointsRow`, or `JobReportRow.InstallationTypes`.

- [ ] **Step 2: Update create logic**

In `CreateAsyncCoreAsync`, replace the `if (request.Work?.InstallationTypes?.Count > 0)` block with:

```csharp
if (request.Work?.InstallationTypes?.Count > 0)
{
    await AddSelectedInstallationsAsync(organizationId, reportId, request.Work.InstallationTypes, now, cancellationToken);
}
```

- [ ] **Step 3: Update update logic**

In `UpdateAsyncCoreAsync`, replace the `if (request.Work?.InstallationTypes is not null)` block with:

```csharp
if (request.Work?.InstallationTypes is not null)
{
    var existingInstallations = await _dbContext.JobReportInstallations
        .Where(it => it.JobReportId == id && it.OrganizationId == organizationId)
        .ToListAsync(cancellationToken);
    _dbContext.JobReportInstallations.RemoveRange(existingInstallations);

    await AddSelectedInstallationsAsync(organizationId, id, request.Work.InstallationTypes, now, cancellationToken);
}
```

- [ ] **Step 4: Add selected-installation insertion helper**

Add this private method before `LoadLinksAsync`:

```csharp
private async Task AddSelectedInstallationsAsync(
    Guid organizationId,
    Guid jobReportId,
    IReadOnlyList<CreateInstallationTypeRequest> installationRequests,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    var definitionIds = installationRequests.Select(request => request.Id).Distinct().ToArray();
    var definitions = await _dbContext.InstallationTypeDefinitions
        .AsNoTracking()
        .Where(definition => definition.OrganizationId == organizationId && definitionIds.Contains(definition.Id))
        .Include(definition => definition.Mappings)
        .ToDictionaryAsync(definition => definition.Id, cancellationToken);

    for (var installationIndex = 0; installationIndex < installationRequests.Count; installationIndex++)
    {
        var installationRequest = installationRequests[installationIndex];
        if (!definitions.TryGetValue(installationRequest.Id, out var definition))
        {
            continue;
        }

        var selectedInstallation = new JobReportInstallationRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobReportId = jobReportId,
            InstallationTypeDefinitionId = installationRequest.Id,
            SortOrder = installationIndex + 1
        };

        _dbContext.JobReportInstallations.Add(selectedInstallation);

        var mappingsByPair = definition.Mappings.ToDictionary(mapping => (mapping.ControlCategoryId, mapping.ControlPointId));
        var categories = installationRequest.Categories ?? [];
        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var categoryRequest = categories[categoryIndex];
            var selectedCategory = new JobReportInstallationCategoryRow
            {
                Id = Guid.NewGuid(),
                JobReportInstallationId = selectedInstallation.Id,
                ControlCategoryId = categoryRequest.Id,
                SortOrder = categoryIndex + 1,
                JobReportInstallation = selectedInstallation
            };

            _dbContext.JobReportInstallationCategories.Add(selectedCategory);

            var controlPoints = categoryRequest.ControlPoints ?? [];
            for (var controlPointIndex = 0; controlPointIndex < controlPoints.Count; controlPointIndex++)
            {
                var controlPointRequest = controlPoints[controlPointIndex];
                mappingsByPair.TryGetValue((categoryRequest.Id, controlPointRequest.Id), out var mapping);

                _dbContext.JobReportInstallationControlPoints.Add(new JobReportInstallationControlPointRow
                {
                    JobReportInstallationCategoryId = selectedCategory.Id,
                    ControlPointId = controlPointRequest.Id,
                    SortOrder = controlPointRequest.SortOrder ?? mapping?.SortOrder ?? controlPointIndex + 1,
                    IsRequired = controlPointRequest.IsRequired ?? mapping?.IsRequired ?? false,
                    JobReportInstallationCategory = selectedCategory
                });
            }
        }
    }
}
```

- [ ] **Step 5: Update list query**

Replace the `installationTypesByReport` query with:

```csharp
var installationTypesByReport = await _dbContext.JobReportInstallations
    .AsNoTracking()
    .Where(it => it.OrganizationId == query.OrganizationId && reportIds.Contains(it.JobReportId))
    .Include(it => it.InstallationTypeDefinition)
    .GroupBy(it => it.JobReportId)
    .ToDictionaryAsync(
        g => g.Key,
        g => g.OrderBy(it => it.SortOrder).Select(it => it.InstallationTypeDefinition.Name).ToArray() as IReadOnlyList<string>,
        cancellationToken);
```

- [ ] **Step 6: Update get-single includes**

Replace the old include chain with:

```csharp
var row = await _dbContext.JobReports
    .AsNoTracking()
    .Include(r => r.Installations)
        .ThenInclude(i => i.InstallationTypeDefinition)
    .Include(r => r.Installations)
        .ThenInclude(i => i.Categories)
            .ThenInclude(c => c.ControlCategory)
    .Include(r => r.Installations)
        .ThenInclude(i => i.Categories)
            .ThenInclude(c => c.ControlPoints)
                .ThenInclude(cp => cp.ControlPoint)
    .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);
```

- [ ] **Step 7: Update response mapper**

In `ToResponse`, replace the `installationTypes` projection with:

```csharp
var installationTypes = row.Installations?
    .OrderBy(it => it.SortOrder)
    .Select(it =>
    {
        var categories = it.Categories?
            .OrderBy(category => category.SortOrder)
            .Select(category => new InstallationTypeCategoryResponse(
                category.ControlCategory.Id,
                category.ControlCategory.Name,
                category.SortOrder,
                category.ControlPoints
                    .OrderBy(cp => cp.SortOrder)
                    .Select(cp => new InstallationTypeControlPointResponse(
                        cp.ControlPoint.Id,
                        cp.ControlPoint.Name,
                        cp.ControlPoint.Description,
                        cp.SortOrder,
                        cp.IsRequired,
                        cp.ControlPoint.IsChecked))
                    .ToArray()))
            .ToArray() ?? [];

        return new InstallationTypeResponse(
            it.InstallationTypeDefinition.Id,
            it.InstallationTypeDefinition.Name,
            null,
            it.SortOrder,
            categories);
    })
    .ToArray() ?? [];
```

- [ ] **Step 8: Run build to verify GREEN**

Run: `dotnet build src/BE/WorkslipApi/Workslip.Api.csproj`

Expected: PASS.

---

### Task 6: Seeder Update

**Files:**
- Modify: `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/DatabaseInstallationSeeder.cs`

- [ ] **Step 1: Run build to verify current seeder is still broken**

Run: `dotnet build src/BE/WorkslipApi/Workslip.Api.csproj`

Expected: FAIL if `DatabaseInstallationSeeder` still references `InstallationTypeRow` or `InstallationControlPointRow`; otherwise continue to Step 2 to align seed data with the new model.

- [ ] **Step 2: Replace seeded job installation rows**

Replace the old `installationTypes` and `installationControlPoints` construction with this structure after `context.InstallationTypeDefinitionMappings.AddRange(definitionMappings);`:

```csharp
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
```

- [ ] **Step 3: Remove old AddRange calls**

Remove these calls from the seeder:

```csharp
context.InstallationTypeRow.AddRange(installationTypes);
context.InstallationControlPointsRow.AddRange(installationControlPoints);
```

- [ ] **Step 4: Run build to verify GREEN**

Run: `dotnet build src/BE/WorkslipApi/Workslip.Api.csproj`

Expected: PASS.

---

### Task 7: Migration And Full Verification

**Files:**
- Modify generated migration files only after explicit approval to generate migrations.

- [ ] **Step 1: Ask for migration approval**

Ask the user: `May I generate an EF migration for the job installation selection table changes?`

Expected: Continue only if the user approves.

- [ ] **Step 2: Generate migration after approval**

Run from `src/BE/WorkslipApi`:

`dotnet ef migrations add JobInstallationSelections --project Workslip.Api.csproj --startup-project Workslip.Api.csproj`

Expected: new migration files under the EF migrations output location for `Workslip.Api`.

- [ ] **Step 3: Inspect migration before any database update**

Run: `git diff -- src/BE/WorkslipApi`

Expected: migration creates `JobReportInstallations`, `JobReportInstallationCategories`, and `JobReportInstallationControlPoints`. It should not update a real database.

- [ ] **Step 4: Run full tests**

Run: `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Run full build**

Run: `dotnet build src/BE/WorkslipApi/Workslip.Api.csproj`

Expected: PASS.

- [ ] **Step 6: Check changed files**

Run: `git status --short`

Expected: only intended domain, application, infrastructure, tests, and approved migration files are changed.

---

## Self-Review

Spec coverage:
- Reference data remains in `InstallationTypeDefinitions`, `ControlCategories`, `ControlPoints`, and `InstallationTypeDefinitionMappings` via Task 4.
- Job-specific selected installations/categories/control points are added via Task 4.
- Request shape changes from name to reference IDs via Task 2.
- Duplicate and allowed-combination validation is covered by Tasks 2 and 3.
- Repository create/update/fetch behavior is covered by Task 5.
- Seeder and migration path are covered by Tasks 6 and 7.

Completion scan:
- The plan contains concrete paths, commands, and code snippets for each implementation step.

Type consistency:
- Request records use `CreateInstallationTypeRequest(Guid Id, ...)` consistently.
- Domain rows use `JobReportInstallationRow`, `JobReportInstallationCategoryRow`, and `JobReportInstallationControlPointRow` consistently.
- Response uses the existing `InstallationTypeResponse`, `InstallationTypeCategoryResponse`, and `InstallationTypeControlPointResponse` records.
