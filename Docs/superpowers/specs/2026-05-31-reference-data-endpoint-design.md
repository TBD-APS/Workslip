# Reference Data Endpoint

Serve installation type definitions (with their category/control-point hierarchy) and work kinds as long-cached reference data for the job form.

## Problem

The job creation/edit form needs to show the user what options are available:

- Which installation types exist (gas, water, drain, heating)
- For each installation type, which control categories and control points apply
- Which work kinds exist

These are currently only returned embedded in job responses or used internally. No standalone endpoint exists.

## Solution

Single endpoint `GET /api/reference-data` returning all form-relevant reference data, aggressively cached.

### New Entities

**InstallationTypeDefinitionRow** (`dbo.InstallationTypeDefinitions`):

| Column | Type | Notes |
|---|---|---|
| Id | guid (PK) | |
| OrganizationId | guid (FK → Organizations) | |
| Name | nvarchar(200) | e.g. "Gasinstallation" |
| SortOrder | int | |

Unique index: `(OrganizationId, Name)`

**InstallationTypeDefinitionMappingRow** (`dbo.InstallationTypeDefinitionMappings`):

| Column | Type | Notes |
|---|---|---|
| InstallationTypeDefinitionId | guid (FK → InstallationTypeDefinitions) | PK part 1 |
| ControlCategoryId | guid (FK → ControlCategories) | PK part 2 |
| ControlPointId | guid (FK → ControlPoints) | PK part 3 |
| SortOrder | int | |
| IsRequired | bit | |

Composite PK: `(InstallationTypeDefinitionId, ControlCategoryId, ControlPointId)`
All FKs use restrict delete.

No changes to existing per-job entities (`InstallationTypeRow`, `InstallationControlPointRow`).

### Response Shape

```
GET /api/reference-data → 200 OK (or 304 Not Modified)
```

```json
{
  "installationTypes": [
    {
      "id": "guid",
      "name": "Gasinstallation",
      "sortOrder": 1,
      "categories": [
        {
          "id": "guid",
          "name": "Forundersøgelse",
          "sortOrder": 1,
          "controlPoints": [
            {
              "id": "guid",
              "name": "Ansøgning på gas",
              "description": null,
              "sortOrder": 1,
              "isRequired": true
            }
          ]
        }
      ]
    }
  ],
  "workKinds": [
    {
      "id": "NewInstallation",
      "label": "Ny installation",
      "requiresCustomWorkKind": false,
      "sortOrder": 1
    }
  ],
  "closureFlags": [
    {
      "id": "NotCompleted",
      "label": "Ikke færdig",
      "isExclusive": true,
      "sortOrder": 1
    },
    {
      "id": "Completed",
      "label": "Færdig",
      "isExclusive": false,
      "sortOrder": 2
    }
  ]
}
```

### Response DTOs

```csharp
public sealed record ReferenceDataResponse(
    IReadOnlyList<InstallationTypeDefinitionResponse> InstallationTypes,
    IReadOnlyList<WorkKindResponse> WorkKinds,
    IReadOnlyList<ClosureFlagResponse> ClosureFlags);

public sealed record InstallationTypeDefinitionResponse(
    Guid Id, string Name, int SortOrder,
    IReadOnlyList<InstallationTypeCategoryResponse> Categories);

public sealed record InstallationTypeCategoryResponse(
    Guid Id, string Name, int SortOrder,
    IReadOnlyList<InstallationTypeControlPointResponse> ControlPoints);

public sealed record InstallationTypeControlPointResponse(
    Guid Id, string Name, string? Description, int SortOrder, bool IsRequired);

public sealed record WorkKindResponse(
    string Id, string Label, bool RequiresCustomWorkKind, int SortOrder);

public sealed record ClosureFlagResponse(
    string Id, string Label, bool IsExclusive, int SortOrder);
```

### Layers

**Domain** — new entity files in `Workslip.Domain/Models/`:
- `InstallationTypeDefinitionRow.cs`
- `InstallationTypeDefinitionMappingRow.cs`

**Infrastructure** — new files in `Workslip.Infrastructure/Schema/`:
- EF config for both new entities (table mapping, keys, indexes, FKs)
- `EfReferenceDataRepository.cs` — single query building the tree

**Application** — new files in `Workslip.Application/Jobs/`:
- `ReferenceDataContracts.cs` — response DTOs
- `IReferenceDataService.cs` + `ReferenceDataService.cs`
- Extend `DatabaseInstaller.cs` registration

**API** — new file `Endpoints/ReferenceDataEndpoints.cs` or inline in existing:
- `GET /api/reference-data`
- Uses `CachedOk` helper pattern

### Repository Query

```sql
SELECT d.*, m.*, cc.*, cp.*
FROM InstallationTypeDefinitions d
LEFT JOIN InstallationTypeDefinitionMappings m ON m.InstallationTypeDefinitionId = d.Id
LEFT JOIN ControlCategories cc ON cc.Id = m.ControlCategoryId
LEFT JOIN ControlPoints cp ON cp.Id = m.ControlPointId
WHERE d.OrganizationId = @orgId
ORDER BY d.SortOrder, cc.SortOrder, m.SortOrder
```

Group in memory by definition → category → control point.

Work kinds: `SELECT * FROM JobWorkKinds WHERE IsActive = 1 ORDER BY SortOrder`

Closure flags: `SELECT * FROM JobClosureFlags WHERE IsActive = 1 ORDER BY SortOrder`

### Caching

- `Cache-Control: public, max-age=86400` (24 hours)
- ETag based on combined hash of all returned data
- 304 Not Modified when ETag matches
- ETag algorithm: SHA256 of serialized response (consistent ordering)

### Seed Data

Extend `DatabaseInstallationSeeder.cs` to create:
1. One `InstallationTypeDefinitionRow` per distinct installation type name
2. One `InstallationTypeDefinitionMappingRow` per `ControlPointTemplate` entry

The existing per-job seeding logic is unchanged.

### Error Handling

- Returns `Result<ReferenceDataResponse>` following standard pattern
- `ToHttpResult` maps failures as usual (unauthorized, server error)
- Organization comes from the authenticated user context — no 404 scenario
- Empty lists return `{ installationTypes: [], workKinds: [], closureFlags: [] }` — not an error
