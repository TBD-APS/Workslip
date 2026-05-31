# Reference Data Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `GET /api/reference-data` returning installation type definitions (with category/control-point hierarchy), work kinds, and closure flags — aggressively cached with 24h TTL.

**Architecture:** Two new domain entities + one new EF configuration in SqlDbContext. Repository + Service + Endpoint following the existing IRepository → IService → Endpoint pattern. All existing per-job entities unchanged.

**Tech Stack:** .NET 9, EF Core, Ardalis.Result, HybridCache (via existing infrastructure)

---

## File Structure

**New files (8):**
- `Workslip.Domain/Models/InstallationTypeDefinitionRow.cs`
- `Workslip.Domain/Models/InstallationTypeDefinitionMappingRow.cs`
- `Workslip.Application/Jobs/IReferenceDataRepository.cs`
- `Workslip.Application/Jobs/ReferenceDataContracts.cs`
- `Workslip.Application/Jobs/IReferenceDataService.cs`
- `Workslip.Application/Jobs/ReferenceDataService.cs`
- `Workslip.Infrastructure/Repositories/EfReferenceDataRepository.cs`
- `Endpoints/ReferenceDataEndpoints.cs`

**Modified files (5):**
- `Workslip.Infrastructure/Schema/SqlDbContext.cs` — add DbSets + entity config
- `Workslip.Infrastructure/DependencyInjection.cs` — register repository
- `Workslip.Application/DependencyInjection.cs` — register service
- `Configuration/EndpointConfiguration.cs` — register endpoints
- `Configuration/HttpCacheHeaders.cs` — add `SetPublicLongCache` + `ReferenceDataEtag`
- `Workslip.Infrastructure/Schema/DatabaseInstallationSeeder.cs` — seed definitions + mappings

---

### Task 1: Domain entities

**Files:**
- Create: `Workslip.Domain/Models/InstallationTypeDefinitionRow.cs`
- Create: `Workslip.Domain/Models/InstallationTypeDefinitionMappingRow.cs`

- [ ] **Create InstallationTypeDefinitionRow.cs**

```csharp
namespace Workslip.Domain.Models;

public sealed class InstallationTypeDefinitionRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
```

- [ ] **Create InstallationTypeDefinitionMappingRow.cs**

```csharp
namespace Workslip.Domain.Models;

public sealed class InstallationTypeDefinitionMappingRow
{
    public Guid InstallationTypeDefinitionId { get; set; }
    public InstallationTypeDefinitionRow InstallationTypeDefinition { get; set; } = null!;
    public Guid ControlCategoryId { get; set; }
    public ControlCategoryRow ControlCategory { get; set; } = null!;
    public Guid ControlPointId { get; set; }
    public ControlPointRow ControlPoint { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
}
```

---

### Task 2: EF configuration

**Files:**
- Modify: `Workslip.Infrastructure/Schema/SqlDbContext.cs`

- [ ] **Add DbSet properties** (near line 16, after `JobClosureFlags`):

```csharp
public DbSet<InstallationTypeDefinitionRow> InstallationTypeDefinitions => Set<InstallationTypeDefinitionRow>();
public DbSet<InstallationTypeDefinitionMappingRow> InstallationTypeDefinitionMappings => Set<InstallationTypeDefinitionMappingRow>();
```

- [ ] **Call config methods** (near line 37, after `ConfigureJobClosureFlags`):

```csharp
ConfigureInstallationTypeDefinitions(modelBuilder);
ConfigureInstallationTypeDefinitionMappings(modelBuilder);
```

- [ ] **Add ConfigureInstallationTypeDefinitions** (after `ConfigureJobClosureFlags`):

```csharp
private static void ConfigureInstallationTypeDefinitions(ModelBuilder modelBuilder)
{
    var entity = modelBuilder.Entity<InstallationTypeDefinitionRow>();

    entity.ToTable("InstallationTypeDefinitions");
    entity.HasKey(e => e.Id);

    entity.Property(e => e.Name)
        .HasMaxLength(200)
        .IsRequired();

    entity.Property(e => e.SortOrder)
        .HasDefaultValue(0);

    entity.HasOne<OrganizationRow>()
        .WithMany()
        .HasForeignKey(e => e.OrganizationId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasIndex(e => new { e.OrganizationId, e.Name })
        .IsUnique();

    entity.HasIndex(e => new { e.OrganizationId, e.SortOrder });
}
```

- [ ] **Add ConfigureInstallationTypeDefinitionMappings** (after the above):

```csharp
private static void ConfigureInstallationTypeDefinitionMappings(ModelBuilder modelBuilder)
{
    var entity = modelBuilder.Entity<InstallationTypeDefinitionMappingRow>();

    entity.ToTable("InstallationTypeDefinitionMappings");
    entity.HasKey(e => new { e.InstallationTypeDefinitionId, e.ControlCategoryId, e.ControlPointId });

    entity.Property(e => e.SortOrder)
        .HasDefaultValue(0);

    entity.Property(e => e.IsRequired)
        .HasDefaultValue(false);

    entity.HasOne(e => e.InstallationTypeDefinition)
        .WithMany()
        .HasForeignKey(e => e.InstallationTypeDefinitionId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.ControlCategory)
        .WithMany()
        .HasForeignKey(e => e.ControlCategoryId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasOne(e => e.ControlPoint)
        .WithMany()
        .HasForeignKey(e => e.ControlPointId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasIndex(e => new { e.InstallationTypeDefinitionId, e.ControlCategoryId, e.SortOrder });
}
```

---

### Task 3: Response DTOs

**Files:**
- Create: `Workslip.Application/Jobs/ReferenceDataContracts.cs`

- [ ] **Create ReferenceDataContracts.cs:**

```csharp
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record ReferenceDataResponse(
    IReadOnlyList<InstallationTypeDefinitionResponse> InstallationTypes,
    IReadOnlyList<WorkKindResponse> WorkKinds,
    IReadOnlyList<ClosureFlagResponse> ClosureFlags);

public sealed record InstallationTypeDefinitionResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<DefinitionCategoryResponse> Categories);

public sealed record DefinitionCategoryResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<DefinitionControlPointResponse> ControlPoints);

public sealed record DefinitionControlPointResponse(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsRequired);

public sealed record WorkKindResponse(
    string Id,
    string Label,
    bool RequiresCustomWorkKind,
    int SortOrder);

public sealed record ClosureFlagResponse(
    string Id,
    string Label,
    bool IsExclusive,
    int SortOrder);
```

Note: Names `DefinitionCategoryResponse` / `DefinitionControlPointResponse` avoid conflict with existing `InstallationTypeCategoryResponse` / `InstallationTypeControlPointResponse` in `JobContracts.cs`.

---

### Task 4: Repository interface

**Files:**
- Create: `Workslip.Application/Jobs/IReferenceDataRepository.cs`

- [ ] **Create the interface:**

```csharp
namespace Workslip.Application.Jobs;

public interface IReferenceDataRepository
{
    Task<ReferenceDataResponse> GetAsync(Guid organizationId, CancellationToken cancellationToken);
}
```

---

### Task 5: Repository implementation

**Files:**
- Create: `Workslip.Infrastructure/Repositories/EfReferenceDataRepository.cs`

- [ ] **Create the repository:**

```csharp
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfReferenceDataRepository : IReferenceDataRepository
{
    private readonly SqlDbContext _dbContext;

    public EfReferenceDataRepository(SqlDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReferenceDataResponse> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.InstallationTypeDefinitions
            .AsNoTracking()
            .Where(d => d.OrganizationId == organizationId)
            .OrderBy(d => d.SortOrder)
            .Select(d => new InstallationTypeDefinitionResponse(
                d.Id,
                d.Name,
                d.SortOrder,
                d.Mappings
                    .OrderBy(m => m.ControlCategory.SortOrder)
                    .ThenBy(m => m.SortOrder)
                    .GroupBy(m => new { m.ControlCategory.Id, m.ControlCategory.Name, m.ControlCategory.SortOrder })
                    .Select(g => new DefinitionCategoryResponse(
                        g.Key.Id,
                        g.Key.Name,
                        g.Key.SortOrder,
                        g.Select(m => new DefinitionControlPointResponse(
                            m.ControlPoint.Id,
                            m.ControlPoint.Name,
                            m.ControlPoint.Description,
                            m.SortOrder,
                            m.IsRequired))
                            .ToArray()))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        var workKinds = await _dbContext.JobWorkKinds
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.SortOrder)
            .Select(w => new WorkKindResponse(w.Id, w.Label, w.RequiresCustomWorkKind, w.SortOrder))
            .ToArrayAsync(cancellationToken);

        var closureFlags = await _dbContext.JobClosureFlags
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .Select(f => new ClosureFlagResponse(f.Id, f.Label, f.IsExclusive, f.SortOrder))
            .ToArrayAsync(cancellationToken);

        return new ReferenceDataResponse(definitions, workKinds, closureFlags);
    }
}
```

Wait — this uses `d.Mappings` but we haven't defined a navigation property on `InstallationTypeDefinitionRow`. I need to add that. Let me update the entity.

- [ ] **Update InstallationTypeDefinitionRow to add Mappings nav property:**

```csharp
public sealed class InstallationTypeDefinitionRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public ICollection<InstallationTypeDefinitionMappingRow> Mappings { get; set; } = [];
}
```

---

### Task 6: Service layer

**Files:**
- Create: `Workslip.Application/Jobs/IReferenceDataService.cs`
- Create: `Workslip.Application/Jobs/ReferenceDataService.cs`

- [ ] **Create IReferenceDataService.cs:**

```csharp
using Ardalis.Result;

namespace Workslip.Application.Jobs;

public interface IReferenceDataService
{
    Task<Result<ReferenceDataResponse>> GetAsync(CancellationToken cancellationToken);
}
```

- [ ] **Create ReferenceDataService.cs:**

```csharp
using Ardalis.Result;
using Workslip.Application.Auth;

namespace Workslip.Application.Jobs;

public sealed class ReferenceDataService : IReferenceDataService
{
    private readonly IReferenceDataRepository _repository;
    private readonly ICurrentUserContext _currentUser;

    public ReferenceDataService(IReferenceDataRepository repository, ICurrentUserContext currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<ReferenceDataResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null)
            return Result<ReferenceDataResponse>.Forbidden();

        var data = await _repository.GetAsync(orgId.Value, cancellationToken);
        return Result<ReferenceDataResponse>.Success(data);
    }
}
```

---

### Task 7: Cache helpers

**Files:**
- Modify: `Configuration/HttpCacheHeaders.cs`

- [ ] **Add SetPublicLongCache method:**

```csharp
public static void SetPublicLongCache(HttpContext context, string etag)
{
    context.Response.Headers.CacheControl = "public, max-age=86400";
    context.Response.Headers.ETag = etag;
    context.Response.Headers.Vary = "Accept-Encoding";
}
```

- [ ] **Add ReferenceDataEtag method:**

```csharp
public static string ReferenceDataEtag(ReferenceDataResponse data)
{
    var sb = new StringBuilder("reference-data:");
    foreach (var type in data.InstallationTypes)
    {
        sb.Append(type.Id).Append(type.SortOrder);
        foreach (var cat in type.Categories)
        {
            sb.Append(cat.Id).Append(cat.SortOrder);
            foreach (var cp in cat.ControlPoints)
                sb.Append(cp.Id).Append(cp.SortOrder).Append(cp.IsRequired);
        }
    }
    foreach (var wk in data.WorkKinds)
        sb.Append(wk.Id).Append(wk.SortOrder).Append(wk.RequiresCustomWorkKind);
    foreach (var cf in data.ClosureFlags)
        sb.Append(cf.Id).Append(cf.SortOrder).Append(cf.IsExclusive);
    return ToWeakEtag(sb.ToString());
}
```

Add `using System.Text;` to the top of the file.

- [ ] **Build and verify**

---

### Task 8: Endpoint

**Files:**
- Create: `Endpoints/ReferenceDataEndpoints.cs`

- [ ] **Create ReferenceDataEndpoints.cs:**

```csharp
using Workslip.Api.Helpers;
using Workslip.Application.Jobs;

namespace Workslip.Api.Endpoints;

public static class ReferenceDataEndpoints
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reference-data")
            .WithTags("reference-data")
            .RequireAuthorization(AuthPolicies.RequireUser);

        group.MapGet("/", async (HttpContext httpContext, IReferenceDataService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(cancellationToken);
            if (!result.IsSuccess)
                return ResultExtensions.ToHttpResult(result);

            var etag = HttpCacheHeaders.ReferenceDataEtag(result.Value);
            HttpCacheHeaders.SetPublicLongCache(httpContext, etag);

            return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(result.Value);
        });

        return app;
    }
}
```

---

### Task 9: DI registration

**Files:**
- Modify: `Workslip.Infrastructure/DependencyInjection.cs`
- Modify: `Workslip.Application/DependencyInjection.cs`

- [ ] **Register repository** in `Workslip.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<IReferenceDataRepository, EfReferenceDataRepository>();
```

(Add alongside existing repository registrations)

- [ ] **Register service** in `Workslip.Application/DependencyInjection.cs`:

```csharp
services.AddScoped<IReferenceDataService, ReferenceDataService>();
```

(Add alongside existing service registrations)

---

### Task 10: Endpoint registration

**Files:**
- Modify: `Configuration/EndpointConfiguration.cs`

- [ ] **Add MapReferenceDataEndpoints call:**

```csharp
app.MapReferenceDataEndpoints();
```

(Add alongside the existing endpoint registrations, e.g. after `app.MapJobEndpoints();`)

---

### Task 11: Seed data

**Files:**
- Modify: `Workslip.Infrastructure/Schema/DatabaseInstallationSeeder.cs`

- [ ] **Add seeding for definitions and mappings** (alongside existing seeding, create definition rows for each distinct installation type name, then create mapping rows from the same `ControlPointTemplate` list). Search for where the seeder creates `InstallationTypeRow` entries to find the right insertion point. Create one `InstallationTypeDefinitionRow` per distinct installation type name, then for each `ControlPointTemplate` entry, create an `InstallationTypeDefinitionMappingRow`.

The exact insertion point depends on how the seeder is structured — likely after `ControlCategoryRow` and `ControlPointRow` creation, before per-job seeding. The pattern to follow:

```csharp
var definitionMap = new Dictionary<string, InstallationTypeDefinitionRow>();
foreach (var typeName in templates.Select(t => t.InstallationTypeName).Distinct())
{
    var definition = new InstallationTypeDefinitionRow
    {
        Id = Guid.NewGuid(),
        OrganizationId = organization.Id,
        Name = typeName,
        SortOrder = Array.IndexOf(new[] { "Gasinstallation", "Vandinstallation", "Afløbsinstallation", "Varmeinstallation" }, typeName) + 1
    };
    db.InstallationTypeDefinitions.Add(definition);
    definitionMap[typeName] = definition;
}

foreach (var template in templates)
{
    var definition = definitionMap[template.InstallationTypeName];
    var category = categoryMap[template.CategoryName];
    var controlPoint = controlPointMap[template.ControlPointName];

    db.InstallationTypeDefinitionMappings.Add(new InstallationTypeDefinitionMappingRow
    {
        InstallationTypeDefinitionId = definition.Id,
        ControlCategoryId = category.Id,
        ControlPointId = controlPoint.Id,
        SortOrder = template.Order,
        IsRequired = false
    });
}
```

(Where `categoryMap` and `controlPointMap` are dictionaries built from the already-seeded `ControlCategoryRow` and `ControlPointRow` entries.)

---

### Task 12: Build and verify

- [ ] **Build the entire solution:**

```bash
dotnet build .\Workslip.Api.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Build all projects** (not just API — includes Domain and Application):

```bash
dotnet build ..\WorkslipApi.sln
```
Expected: Build succeeded, 0 errors.
