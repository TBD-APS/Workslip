using AutoBogus;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public static class DatabaseSeeder
{
    public static async Task Seed(SqlDbContext db)
    {
        await NormalizeExclusiveClosureFlagSelectionsAsync(db);

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
            new() { Id = Guid.NewGuid(), NormalizedLabel = "NewInstallation", Label = "Ny installation", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 1 },
            new() { Id = Guid.NewGuid(), NormalizedLabel = "ChangeOfInstallation", Label = "Ændring af installation", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 2},
            new() { Id = Guid.NewGuid(), NormalizedLabel = "RepairWork", Label = "Reparationsarbejde", RequiresCustomWorkKind = false, IsActive = true, SortOrder = 3 },
            new() { Id = Guid.NewGuid(), NormalizedLabel = "ServiceOther", Label = "Service/Andet", RequiresCustomWorkKind = true, IsActive = true, SortOrder = 4 }
        };

        var jobClosureFlags = new List<JobClosureFlagRow>
        {
            new() { Id = Guid.NewGuid(), NormalizedLabel = "NotCompleted", Label = "Ikke færdig", IsExclusive = true, IsActive = true, SortOrder = 1, UpdatedAt = now },
            new() { Id = Guid.NewGuid(), NormalizedLabel = "Completed", Label = "Færdig", IsExclusive = false, IsActive = true, SortOrder = 2, UpdatedAt = now },
            new() { Id = Guid.NewGuid(), NormalizedLabel = "OperationMaintenanceInstructions", Label = "Drift og vedligeholdelses-instruktioner", IsExclusive = false, IsActive = true, SortOrder = 3, UpdatedAt = now },
            new() { Id = Guid.NewGuid(), NormalizedLabel = "ReadyForInvoice", Label = "Klar til faktura", IsExclusive = false, IsActive = true, SortOrder = 3, UpdatedAt = now }
        };

        var customers = new Faker<CustomerRow>()
            .RuleFor(x => x.Id, _ => Guid.NewGuid())
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.Name, f => f.Company.CompanyName())
            .RuleFor(x => x.Address, f => f.Address.FullAddress())
            .RuleFor(x => x.Email, f => f.Internet.Email())
            .RuleFor(x => x.ContactPerson, f => f.Name.FullName())
            .RuleFor(x => x.Phone, f => f.Phone.PhoneNumber("####-####"))
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(50);

        var users = new Faker<UserDataRow>()
            .RuleFor(x => x.Id, _ => Guid.NewGuid())
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.DisplayName, f => f.Name.FullName())
            .RuleFor(x => x.Email, f => f.Internet.Email())
            .RuleFor(x => x.Phone, f => f.Phone.PhoneNumber("####-####"))
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

        var adminUser = new Faker<UserDataRow>()
            .RuleFor(x => x.Id, _ => new Guid("A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F"))
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.DisplayName, _ => "Admin Test")
            .RuleFor(x => x.Email, _ => "admin@17v3ygzs.mailosaur.net")
            .RuleFor(x => x.Phone, _ => "10000001")
            .RuleFor(x => x.Role, _ => "Admin")
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(1);

        var regularUser = new Faker<UserDataRow>()
            .RuleFor(x => x.Id, _ => new Guid("B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F"))
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.DisplayName, _ => "User Test")
            .RuleFor(x => x.Email, _ => "user@17v3ygzs.mailosaur.net")
            .RuleFor(x => x.Phone, _ => "10000002")
            .RuleFor(x => x.Role, _ => "User")
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(1);

        users.AddRange(rbjUser);
        users.AddRange(adminUser);
        users.AddRange(regularUser);

        var statuses = new[] { "Draft", "Submitted", "InReview", "Approved", "Rejected", "Archived" };

        var jobs = new Faker<JobReportRow>()
            .RuleFor(x => x.Id, _ => Guid.NewGuid())
            .RuleFor(x => x.OrganizationId, _ => organization.Id)
            .RuleFor(x => x.CustomerId, f => f.PickRandom(customers).Id)
            .RuleFor(x => x.ReportNumber, f => f.Random.Replace("####"))
            .RuleFor(x => x.Status, f => f.PickRandom(statuses))
            .RuleFor(x => x.ReportDate, f => f.Date.Past(1).Date)
            .RuleFor(x => x.TaskDescription, f => f.Lorem.Sentence())
            .RuleFor(x => x.WorkKindId, f => f.PickRandom(jobWorkKinds).Id)
            .RuleFor(x => x.CustomWorkKind, f => f.Random.Bool(0.15f) ? f.Commerce.ProductName() : null)
            .RuleFor(x => x.IsSoftDeleted, _ => false)
            .RuleFor(x => x.CreatedAt, f => f.Date.PastOffset(1))
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(50);

        var usedPairs = new HashSet<(Guid, Guid)>();
        var assignments = new List<JobAssignmentRow>();
        var faker = new Faker();

        // Ensure dev test users have explicit assignments so the FE can demo the role split.
        var regularUserId = new Guid("B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F");
        var adminUserId = new Guid("A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F");
        foreach (var job in jobs.Take(5))
        {
            usedPairs.Add((job.Id, regularUserId));
            assignments.Add(new JobAssignmentRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                ReportId = job.Id,
                UserId = regularUserId,
                AssignedAt = faker.Date.PastOffset(1)
            });
        }
        foreach (var job in jobs.Take(10))
        {
            if (!usedPairs.Add((job.Id, adminUserId))) continue;
            assignments.Add(new JobAssignmentRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                ReportId = job.Id,
                UserId = adminUserId,
                AssignedAt = faker.Date.PastOffset(1)
            });
        }

        foreach (var job in jobs)
        {
            var userId = faker.PickRandom(users).Id;
            if (!usedPairs.Add((job.Id, userId)))
            {
                continue;
            }

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

        var usersByJob = assignments
            .GroupBy(a => a.ReportId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.UserId).ToArray());

        var worksheets = new List<WorksheetRow>();
        var jobLinks = new List<JobReportLinkRow>();

        var existingLinks = new HashSet<(Guid SourceId, Guid TargetId)>();

        var random = new Faker();
        foreach (var job in jobs)
        {

            // Example: only some reports get links
            if (!random.Random.Bool(0.25f))
            {
                continue;
            }

            //Adds links between job reports
            var possibleTargets = jobs.Where(x =>
                                        x.Id != job.Id &&
                                        x.OrganizationId == job.OrganizationId)
                                       .ToList();

            if (possibleTargets.Count == 0)
            {
                continue;
            }

            var targetCount = random.Random.Int(1, Math.Min(3, possibleTargets.Count));

            var targets = random
                .PickRandom(possibleTargets, targetCount)
                .DistinctBy(x => x.Id)
                .ToArray();

            foreach (var targetReport in targets)
            {
                if (!existingLinks.Add((job.Id, targetReport.Id)))
                {
                    continue;
                }

                var link = new AutoFaker<JobReportLinkRow>()
                    .RuleFor(x => x.Id, f => f.Random.Guid())
                    .RuleFor(x => x.OrganizationId, _ => job.OrganizationId)
                    .RuleFor(x => x.SourceReportId, _ => job.Id)
                    .RuleFor(x => x.TargetReportId, _ => targetReport.Id)
                    .RuleFor(x => x.CreatedAt, f => f.Date.PastOffset(1))
                    .Generate();

                jobLinks.Add(link);
            }

            var assignedUserIds = usersByJob.GetValueOrDefault(job.Id, []);
            if (assignedUserIds.Length == 0) 
                continue;

            //Adds worksheets to job report
            var entryCount = faker.Random.Int(1, 5);
            
            for (var i = 0; i < entryCount; i++)
            {
                var userId = faker.PickRandom(assignedUserIds);
                var workDate = faker.Date.Past(1);

                worksheets.Add(new WorksheetRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organization.Id,
                    JobId = job.Id,
                    UserId = userId,
                    WorkDate = workDate,
                    HoursWorked = Math.Round(faker.Random.Decimal(1, 8) * 4, MidpointRounding.AwayFromZero) / 4,
                    SleptOnJob = faker.Random.Bool(0.1f),
                    CreatedAt = job.CreatedAt,
                    UpdatedAt = job.UpdatedAt
                });
            }
        }

        var jobClosureFlagJoins = new List<JobReportClosureFlagRow>();
        var exclusiveClosureFlags = jobClosureFlags.Where(flag => flag.IsExclusive).ToArray();
        var combinableClosureFlags = jobClosureFlags.Where(flag => !flag.IsExclusive).ToArray();
        foreach (var job in jobs)
        {
            var selectedFlags = exclusiveClosureFlags.Length > 0 && faker.Random.Bool(0.25f)
                ? [faker.PickRandom(exclusiveClosureFlags)]
                : faker.PickRandom(combinableClosureFlags, faker.Random.Int(1, combinableClosureFlags.Length)).ToArray();

            var sortOrder = 0;
            foreach (var flag in selectedFlags)
            {
                jobClosureFlagJoins.Add(new JobReportClosureFlagRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organization.Id,
                    JobReportId = job.Id,
                    ClosureFlagId = flag.Id,
                    SortOrder = ++sortOrder
                });
            }
        }

        await InstallationSeeder.Seed(db, organization.Id, jobs);

        db.Organizations.Add(organization);
        await db.JobReports.AddRangeAsync(jobs);
        await db.JobAssignments.AddRangeAsync(assignments);
        await db.JobWorkKinds.AddRangeAsync(jobWorkKinds);
        await db.JobClosureFlags.AddRangeAsync(jobClosureFlags);
        await db.JobReportClosureFlags.AddRangeAsync(jobClosureFlagJoins);
        await db.JobReportLinks.AddRangeAsync(jobLinks);

        await db.Customers.AddRangeAsync(customers);
        await db.Users.AddRangeAsync(users);
        await db.Worksheets.AddRangeAsync(worksheets);

        await db.SaveChangesAsync();
    }

    private static async Task NormalizeExclusiveClosureFlagSelectionsAsync(SqlDbContext db)
    {
        var closureFlagSelections = await db.JobReportClosureFlags
            .Include(selection => selection.ClosureFlag)
            .ToListAsync();

        var invalidSelections = closureFlagSelections
            .GroupBy(selection => selection.JobReportId)
            .Select(group =>
            {
                var selections = group.ToArray();
                var exclusiveSelections = selections
                    .Where(selection => selection.ClosureFlag.IsExclusive)
                    .OrderBy(selection => selection.SortOrder)
                    .ThenBy(selection => selection.ClosureFlag.SortOrder)
                    .ToArray();

                if (exclusiveSelections.Length == 0 || selections.Length == 1)
                {
                    return [];
                }

                var keepSelectionId = exclusiveSelections[0].Id;
                return selections.Where(selection => selection.Id != keepSelectionId).ToArray();
            })
            .SelectMany(selections => selections)
            .ToArray();

        if (invalidSelections.Length == 0)
        {
            return;
        }

        db.JobReportClosureFlags.RemoveRange(invalidSelections);
        await db.SaveChangesAsync();
    }
}
