using Bogus;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public static class DatabaseSeeder
{
    public static async Task Seed(SqlDbContext db)
    {
        if (await db.Organizations.AnyAsync())
        {
            return;
        }

        Randomizer.Seed = new Random(12345);
        var now = DateTimeOffset.UtcNow;

        var organization = new Faker<OrganizationRow>()
            .RuleFor(x => x.Id, f => f.Random.Guid())
            .RuleFor(x => x.Cvr, f => f.Random.Replace("########"))
            .RuleFor(x => x.Name, f => f.Company.CompanyName())
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate();

        var jobWorkKinds = new List<JobWorkKindRow>
        {
            new() { Id = "Installation", Label = "Installation", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 1, UpdatedAt = now },
            new() { Id = "Service", Label = "Service", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 2, UpdatedAt = now },
            new() { Id = "Repair", Label = "Repair", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 3, UpdatedAt = now },
            new() { Id = "Inspection", Label = "Inspection", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 4, UpdatedAt = now }
        };

        var jobClosureFlags = new List<JobClosureFlagRow>
        {
            new() { Id = "Completed", Label = "Completed", IsExclusive = true, IsActive = true, SortOrder = 1, UpdatedAt = now },
            new() { Id = "Partial", Label = "Partial", IsExclusive = false, IsActive = true, SortOrder = 2, UpdatedAt = now },
            new() { Id = "Cancelled", Label = "Cancelled", IsExclusive = true, IsActive = true, SortOrder = 3, UpdatedAt = now }
        };

        var customers = new Faker<CustomerRow>()
            .RuleFor(x => x.Id, _ => Guid.NewGuid())
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.Name, f => f.Company.CompanyName())
            .RuleFor(x => x.Address, f => f.Address.FullAddress())
            .RuleFor(x => x.Email, f => f.Internet.Email())
            .RuleFor(x => x.ContactPerson, f => f.Name.FullName())
            .RuleFor(x => x.Phone, f => f.Phone.PhoneNumber())
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(50);

        var users = new Faker<UserDataRow>()
            .RuleFor(x => x.Id, _ => Guid.NewGuid())
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.DisplayName, f => f.Name.FullName())
            .RuleFor(x => x.Email, f => f.Internet.Email())
            .RuleFor(x => x.Phone, f => f.Phone.PhoneNumber())
            .RuleFor(x => x.Role, _ => "User")
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(50);

        var rbjUser = new Faker<UserDataRow>()
            .RuleFor(x => x.Id, _ => new Guid("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F"))
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.DisplayName, f => "Rasmus Bak Jakobsen")
            .RuleFor(x => x.Email, f => "rbj@17v3ygzs.mailosaur.net")
            .RuleFor(x => x.Phone, f => "28929173")
            .RuleFor(x => x.Role, _ => "SuperAdmin")
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(1);

        users.AddRange(rbjUser);

        var installationTypeIds = new[]
        {
            "WaterHeater", "HeatPump", "SolarPanel", "Boiler", "GasFurnace",
            "ElectricHeater", "Radiator", "FloorHeating", "VentilationUnit", "ACUnit"
        };

        var statuses = new[] { "Draft", "Submitted", "InReview", "Approved", "Rejected", "Archived" };

        var jobs = new Faker<JobReportRow>()
            .RuleFor(x => x.Id, _ => Guid.NewGuid())
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.CustomerId, f => f.PickRandom(customers).Id)
            .RuleFor(x => x.ReportNumber, f => f.Random.Replace("####"))
            .RuleFor(x => x.Status, f => f.PickRandom(statuses))
            .RuleFor(x => x.ReportDate, f => f.Date.Past(1).Date)
            .RuleFor(x => x.TaskDescription, f => f.Lorem.Sentence())
            .RuleFor(x => x.InstallationTypesJson, f => System.Text.Json.JsonSerializer.Serialize(f.Make(f.Random.Int(1, 3), () => f.PickRandom(installationTypeIds)).Distinct()))
            .RuleFor(x => x.WorkKind, f => f.PickRandom(jobWorkKinds).Id)
            .RuleFor(x => x.CustomWorkKind, f => f.Random.Bool(0.15f) ? f.Commerce.ProductName() : null)
            .RuleFor(x => x.ClosureFlagsJson, _ => "[]")
            .RuleFor(x => x.IsSoftDeleted, _ => false)
            .RuleFor(x => x.CreatedAt, f => f.Date.PastOffset(1))
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(50);

        var usedPairs = new HashSet<(Guid, Guid)>();
        var assignments = new List<JobAssignmentRow>();
        var faker = new Faker();
        foreach (var job in jobs)
        {
            var userId = faker.PickRandom(users).Id;
            usedPairs.Add((job.Id, userId));
            assignments.Add(new JobAssignmentRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                ReportId = job.Id,
                UserId = userId,
                AssignedAt = faker.Date.PastOffset(1)
            });
        }
        while (assignments.Count < 100)
        {
            var reportId = faker.PickRandom(jobs).Id;
            var userId = faker.PickRandom(users).Id;
            if (usedPairs.Add((reportId, userId)))
            {
                assignments.Add(new JobAssignmentRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organization.Id,
                    ReportId = reportId,
                    UserId = userId,
                    AssignedAt = faker.Date.PastOffset(1)
                });
            }
        }

        db.Organizations.Add(organization);
        db.JobWorkKinds.AddRange(jobWorkKinds);
        db.JobClosureFlags.AddRange(jobClosureFlags);
        db.Customers.AddRange(customers);
        db.Users.AddRange(users);
        db.JobReports.AddRange(jobs);
        db.JobAssignments.AddRange(assignments);

        db.SaveChanges();
    }
}
