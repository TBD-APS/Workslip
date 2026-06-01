# Job Installation Selection Domain Design

## Problem

The current job installation model mixes reference data and job-selected data. `InstallationTypes` is used as job-owned rows, while `InstallationTypeDefinitions`, `ControlCategories`, `ControlPoints`, and `InstallationTypeDefinitionMappings` already represent the reference structure. This makes it unclear whether an installation type is a reusable definition or a selection on one job report.

The domain model must support job reports containing multiple installations. Each selected installation can contain multiple selected categories, and each selected category can contain multiple selected control points. The selected rows should reference the reference data, not copy names or descriptions.

## Goals

- Keep possible installations, categories, and control points as reference data.
- Store job-specific selections separately from reference data.
- Allow each job report to select multiple installations.
- Allow each selected installation to choose a job-specific subset of allowed categories and control points.
- Prevent the same installation definition from being selected more than once on the same job report.
- Validate that selected categories and control points are allowed for the selected installation definition.

## Non-Goals

- Do not snapshot installation/category/control-point names into the job report.
- Do not support selecting the same installation definition multiple times on one job report.
- Do not add free-text custom installations, categories, or control points in this change.

## Reference Data Model

`InstallationTypeDefinitionRow` remains the reference table for possible installations. It owns the configured installation name and sort order for an organization.

`ControlCategoryRow` remains the reference table for possible categories.

`ControlPointRow` remains the reference table for possible control points.

`InstallationTypeDefinitionMappingRow` remains the reference mapping that defines which category/control-point combinations are allowed for an installation definition. This mapping also carries default sort order and required status for a control point under an installation definition.

## Job Selection Model

Add a job-owned selected installation entity:

`JobReportInstallationRow`

- `Id`
- `OrganizationId`
- `JobReportId`
- `InstallationTypeDefinitionId`
- `SortOrder`

Add a selected category entity under each selected installation:

`JobReportInstallationCategoryRow`

- `Id`
- `JobReportInstallationId`
- `ControlCategoryId`
- `SortOrder`

Add a selected control point entity under each selected category:

`JobReportInstallationControlPointRow`

- `JobReportInstallationCategoryId`
- `ControlPointId`
- `SortOrder`
- `IsRequired`

Cardinality:

`JobReport` has many `JobReportInstallations`.

`JobReportInstallation` has many `JobReportInstallationCategories`.

`JobReportInstallationCategory` has many `JobReportInstallationControlPoints`.

Each selected row references the corresponding reference data row. The job-specific rows represent what the user selected for that report.

## Constraints And Indexes

`JobReportInstallations` should have a unique index on `OrganizationId`, `JobReportId`, and `InstallationTypeDefinitionId` so the same installation definition can only be selected once per job.

`JobReportInstallationCategories` should have a unique index on `JobReportInstallationId` and `ControlCategoryId` so a category can only be selected once under the same selected installation.

`JobReportInstallationControlPoints` should use a composite primary key on `JobReportInstallationCategoryId` and `ControlPointId` so a control point can only be selected once under the same selected category.

Foreign keys should cascade from job report to selected installations, from selected installation to selected categories, and from selected category to selected control points. Foreign keys to reference data should use restricted delete behavior.

## API Shape

The create/update job request should use IDs for reference rows:

```json
{
  "work": {
    "installationTypes": [
      {
        "id": "installation-definition-id",
        "categories": [
          {
            "id": "category-id",
            "controlPoints": [
              { "id": "control-point-id" }
            ]
          }
        ]
      }
    ]
  }
}
```

The job response can continue returning nested installation data for the frontend. The response should be built by joining selected job rows to reference rows:

- selected installation row to `InstallationTypeDefinitionRow`
- selected category row to `ControlCategoryRow`
- selected control point row to `ControlPointRow`

## Validation

Request validation should reject:

- duplicate installation definition IDs in the same job request
- duplicate category IDs under the same installation
- duplicate control point IDs under the same category
- unknown installation definition IDs
- unknown category IDs
- unknown control point IDs
- category/control-point selections not allowed by `InstallationTypeDefinitionMappings` for the selected installation definition

Validation should return Ardalis.Result validation errors through the existing service and endpoint response pattern.

## Repository Behavior

On create, the repository should insert the job report first, then insert selected installation/category/control-point rows using the supplied reference IDs.

On update, if `Work.InstallationTypes` is supplied, the repository can replace all selected installation rows for the job report. Cascading delete should remove selected categories and control points. This matches the current replacement-style update behavior and keeps the change minimal.

When fetching a job report, the repository should include selected installations, selected categories, selected control points, and their referenced definition/category/control-point rows. The mapper should build the existing nested response shape from these references.

## Migration Notes

The old job-owned `InstallationTypes` and `InstallationControlPoints` tables should be replaced by the new job-selection tables. Existing seeded/demo job installation rows can be migrated by matching old installation names to `InstallationTypeDefinitions` and moving their linked category/control-point selections into the new selected rows.

The reference data tables should remain: `InstallationTypeDefinitions`, `ControlCategories`, `ControlPoints`, and `InstallationTypeDefinitionMappings`.

## Testing

At minimum, verify that:

- a job can be created with multiple selected installations
- each selected installation can contain multiple selected categories
- each selected category can contain multiple selected control points
- duplicate installation definitions in one job are rejected
- invalid category/control-point combinations are rejected
- job fetch returns selected installations with nested selected categories and control points
- update replaces the selected installation tree for a job
