using AutoBogus;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public static class DatabaseSeeder
{
    private static readonly DevelopmentUserDefinition[] DevelopmentUserDefinitions =
    [
        new(
            new Guid("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Rasmus Bak Jakobsen",
            "rasmusvm6@hotmail.com",
            "28929173",
            Roles.Superadmin),
        new(
            new Guid("A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Niels Petersen",
            "admin@17v3ygzs.mailosaur.net",
            "10000001",
            Roles.Admin),
        new(
            new Guid("B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Arne Arnesen",
            "user@17v3ygzs.mailosaur.net",
            "10000002",
            Roles.User),
        new(
            new Guid("C3C3C3C3-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Auditor Jakobsen",
            "auditor@17v3ygzs.mailosaur.net",
            "10000003",
            Roles.Auditor)
    ];

    public static async Task Seed(SqlDbContext db)
    {
        db.IsSeeding = true;
        try
        {
            await SeedCore(db);
        }
        finally
        {
            db.IsSeeding = false;
        }
    }

    private static async Task SeedCore(SqlDbContext db)
    {
        await NormalizeExclusiveClosureFlagSelectionsAsync(db);

        var existingOrganization = await db.Organizations
            .AsNoTracking()
            .OrderBy(organization => organization.CreatedAt)
            .ThenBy(organization => organization.Id)
            .FirstOrDefaultAsync();

        if (existingOrganization is not null)
        {
            await ReconcileDevelopmentUsersAsync(db, existingOrganization.Id);
            return;
        }

        var faker = new Faker();

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
            .RuleFor(x => x.Phone, f => f.Phone.PhoneNumber("########"))
            .RuleFor(x => x.IsTop, f => f.IndexFaker < 5)
            .RuleFor(x => x.CreatedAt, _ => now)
            .RuleFor(x => x.UpdatedAt, _ => now)
            .Generate(50);

        var users = CreateDevelopmentUsers(organization.Id, now);

        var statuses = new[] { JobStatus.Draft, JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected };

        var jobs = new Faker<JobReportRow>()
            .CustomInstantiator(f =>
            {
                var workKind = f.PickRandom(jobWorkKinds);

                var customer = f.PickRandom(customers);
                var formattedReportNumber = (f.IndexFaker + 1).ToString("D4");

                var row = new JobReportRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organization.Id,
                    CustomerId = customer.Id,
                    CustomerEmail = customer.Email,
                    JobType = JobType.KLS,
                    CustomerAddress = customer.Address,
                    CustomerContactPerson = customer.ContactPerson,
                    DestinationAddress = f.Address.FullAddress(),
                    DestinationZipCode = f.Address.ZipCode(),
                    DestinationCity = f.Address.City(),
                    CustomerName = customer.Name,
                    CustomerPhone = customer.Phone,
                    ReportNumber = formattedReportNumber,
                    Status = f.PickRandom(statuses).ToString(),
                    ReportDate = f.Date.Past(1).Date,
                    TaskDescription = f.Lorem.Sentence(),
                    WorkKindId = workKind.Id,
                    CustomWorkKind = workKind.RequiresCustomWorkKind
                        ? f.Commerce.Product()
                        : null,
                    IsSoftDeleted = false,
                    CreatedAt = f.Date.PastOffset(1),
                    UpdatedAt = now
                };

                return row;

            })
            .Generate(50);

        var usedPairs = new HashSet<(Guid, Guid)>();
        var assignments = new List<JobAssignmentRow>();
      

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

        var assignableUsers = users.Where(u => u.Role is Roles.User or Roles.Admin).ToList();
        foreach (var job in jobs)
        {
            var userId = faker.PickRandom(assignableUsers).Id;
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
            var userId = faker.PickRandom(assignableUsers).Id;
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

        AddYearlyDemoWorksheets(
            worksheets,
            jobs,
            organization.Id,
            regularUserId,
            now,
            seed: 701,
            maxEntriesPerMonth: 16);
        AddYearlyDemoWorksheets(
            worksheets,
            jobs,
            organization.Id,
            adminUserId,
            now,
            seed: 902,
            maxEntriesPerMonth: 18);

        var jobClosureFlagJoins = new List<JobReportClosureFlagRow>();
        foreach (var job in jobs)
        {
            var selectedFlags = faker.PickRandom(jobClosureFlags, faker.Random.Int(1, jobClosureFlags.Count)).ToList();

            if(selectedFlags.Any(x => x.NormalizedLabel == "OperationMaintenanceInstructions") && selectedFlags.Count == 1)
                selectedFlags.Add(jobClosureFlags[1]);
            
            var notCompleted = selectedFlags.FirstOrDefault(f => f.NormalizedLabel == ClosureFlagLabels.NotCompleted);
            if (notCompleted is not null)
            {
                selectedFlags.RemoveAll(f =>
                    f.NormalizedLabel == ClosureFlagLabels.Completed || f.NormalizedLabel == ClosureFlagLabels.ReadyForInvoice);
                if (selectedFlags.Count == 0)
                    selectedFlags.Add(notCompleted);
            }

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

    private static void AddYearlyDemoWorksheets(
        List<WorksheetRow> worksheets,
        IReadOnlyList<JobReportRow> jobs,
        Guid organizationId,
        Guid userId,
        DateTimeOffset now,
        int seed,
        int maxEntriesPerMonth)
    {
        var random = new Random(seed);
        var demoJobs = jobs
            .Where(job => job.OrganizationId == organizationId && !job.IsSoftDeleted)
            .OrderBy(job => job.ReportNumber)
            .Take(18)
            .ToArray();

        if (demoJobs.Length == 0)
        {
            return;
        }

        var year = now.Year;
        for (var month = 1; month <= 12; month++)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var entryCount = Math.Min(maxEntriesPerMonth, random.Next(10, maxEntriesPerMonth + 1));
            var usedDates = new HashSet<DateTime>();

            for (var index = 0; index < entryCount; index++)
            {
                var workDate = NextWeekday(year, month, daysInMonth, random, usedDates);
                var job = demoJobs[random.Next(demoJobs.Length)];
                var hours = QuarterHour(random.Next(8, 33) / 4m);
                var timestamp = new DateTimeOffset(workDate, TimeSpan.Zero).AddHours(8);

                worksheets.Add(new WorksheetRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    JobId = job.Id,
                    UserId = userId,
                    WorkDate = workDate,
                    HoursWorked = hours,
                    SleptOnJob = random.NextDouble() < 0.18,
                    CreatedAt = timestamp,
                    UpdatedAt = timestamp
                });
            }
        }
    }

    private static DateTime NextWeekday(int year, int month, int daysInMonth, Random random, HashSet<DateTime> usedDates)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var candidate = new DateTime(year, month, random.Next(1, daysInMonth + 1));
            if (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || usedDates.Contains(candidate))
            {
                continue;
            }

            usedDates.Add(candidate);
            return candidate;
        }

        var fallback = Enumerable.Range(1, daysInMonth)
            .Select(day => new DateTime(year, month, day))
            .First(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !usedDates.Contains(date));
        usedDates.Add(fallback);
        return fallback;
    }

    private static decimal QuarterHour(decimal hours) => Math.Round(hours * 4, MidpointRounding.AwayFromZero) / 4;

    private static async Task ReconcileDevelopmentUsersAsync(SqlDbContext db, Guid organizationId)
    {
        var developmentUserIds = DevelopmentUserDefinitions
            .Select(definition => definition.Id)
            .ToArray();
        var developmentUserEmails = DevelopmentUserDefinitions
            .Select(definition => definition.Email.ToLowerInvariant())
            .ToArray();

        var existingIdentities = await db.Users
            .Where(user =>
                developmentUserIds.Contains(user.Id) ||
                developmentUserEmails.Contains(user.Email.ToLower()))
            .Select(user => new { user.Id, user.Email })
            .ToListAsync();

        var missingUsers = CreateDevelopmentUsers(organizationId, DateTimeOffset.UtcNow)
            .Where(candidate => existingIdentities.All(existing =>
                existing.Id != candidate.Id &&
                !string.Equals(existing.Email, candidate.Email, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (missingUsers.Length == 0)
        {
            return;
        }

        await db.Users.AddRangeAsync(missingUsers);
        await db.SaveChangesAsync();
    }

    private static List<UserDataRow> CreateDevelopmentUsers(Guid organizationId, DateTimeOffset timestamp) =>
        DevelopmentUserDefinitions
            .Select(definition => new UserDataRow
            {
                Id = definition.Id,
                OrganizationId = organizationId,
                DisplayName = definition.DisplayName,
                Email = definition.Email,
                Phone = definition.Phone,
                Role = definition.Role,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            })
            .ToList();

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
                var notCompleted = selections.FirstOrDefault(s =>
                    s.ClosureFlag.NormalizedLabel == ClosureFlagLabels.NotCompleted);

                if (notCompleted == null)
                    return [];

                return selections.Where(s =>
                    s.Id != notCompleted.Id &&
                    (s.ClosureFlag.NormalizedLabel == ClosureFlagLabels.Completed ||
                     s.ClosureFlag.NormalizedLabel == ClosureFlagLabels.ReadyForInvoice))
                    .ToArray();
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

    private sealed record DevelopmentUserDefinition(
        Guid Id,
        string DisplayName,
        string Email,
        string Phone,
        string Role);
}
