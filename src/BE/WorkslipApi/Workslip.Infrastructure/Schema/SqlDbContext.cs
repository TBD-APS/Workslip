using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class SqlDbContext : DbContext
{
    public SqlDbContext(DbContextOptions<SqlDbContext> options) : base(options)
    {
    }

    public DbSet<OrganizationRow> Organizations => Set<OrganizationRow>();
    public DbSet<UserDataRow> Users => Set<UserDataRow>();
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    public DbSet<JobWorkKindRow> JobWorkKinds => Set<JobWorkKindRow>();
    public DbSet<JobClosureFlagRow> JobClosureFlags => Set<JobClosureFlagRow>();
    public DbSet<JobReportRow> JobReports => Set<JobReportRow>();
    public DbSet<JobAssignmentRow> JobAssignments => Set<JobAssignmentRow>();
    public DbSet<JobReportLinkRow> JobReportLinks => Set<JobReportLinkRow>();
    public DbSet<JobEventRow> JobEvents => Set<JobEventRow>();
    public DbSet<InviteTokenRow> InviteTokens => Set<InviteTokenRow>();
    public DbSet<WorksheetRow> Worksheets => Set<WorksheetRow>();

    public DbSet<InstallationTypeRow> InstallationTypeRow => Set<InstallationTypeRow>();
    public DbSet<ControlPointRow> ControlPointRow => Set<ControlPointRow>();
    public DbSet<ControlCategoryRow> ControlCategoryRow => Set<ControlCategoryRow>();
    public DbSet<InstallationControlPointRow> InstallationControlPointsRow => Set<InstallationControlPointRow>();
    public DbSet<InstallationTypeDefinitionRow> InstallationTypeDefinitions => Set<InstallationTypeDefinitionRow>();
    public DbSet<InstallationTypeDefinitionMappingRow> InstallationTypeDefinitionMappings => Set<InstallationTypeDefinitionMappingRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        ConfigureOrganizations(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureCustomers(modelBuilder);
        ConfigureJobWorkKinds(modelBuilder);
        ConfigureJobClosureFlags(modelBuilder);
        ConfigureJobReports(modelBuilder);
        ConfigureJobAssignments(modelBuilder);
        ConfigureJobReportLinks(modelBuilder);
        ConfigureJobEvents(modelBuilder);
        ConfigureInviteTokens(modelBuilder);
        ConfigureWorksheets(modelBuilder);
        ConfigureInstallationTypes(modelBuilder);
        ConfigureControlCategory(modelBuilder);
        ConfigureControlPoint(modelBuilder);
        ConfigureInstallationControlPoint(modelBuilder);
        ConfigureInstallationTypeDefinitions(modelBuilder);
        ConfigureInstallationTypeDefinitionMappings(modelBuilder);
    }

    private static void ConfigureOrganizations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OrganizationRow>();

        entity.ToTable("Organizations", t =>
        {
            t.HasCheckConstraint("CK_Organizations_Cvr_8Digits",
                "Cvr like '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'");
        });
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Name)
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(e => e.Cvr)
            .HasMaxLength(8)
            .IsRequired();

        entity.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasIndex(e => e.Cvr)
            .IsUnique()
            .HasDatabaseName("UX_Organizations_Cvr");
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserDataRow>();

        entity.ToTable("Users");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(e => e.Email).HasMaxLength(320);
        entity.Property(e => e.Phone).HasMaxLength(80);
        entity.Property(e => e.EntraEmail).HasMaxLength(200);
        entity.Property(e => e.EntraId).HasMaxLength(80);

        entity.Property(e => e.Role)
            .HasMaxLength(80)
            .IsRequired();

        entity.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.OrganizationId, e.Id })
            .IsUnique()
            .HasDatabaseName("UX_Users_Organization_Id");
    }

    private static void ConfigureCustomers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerRow>();

        entity.ToTable("Customers");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Name)
            .HasMaxLength(240)
            .IsRequired();

        entity.Property(e => e.Address).HasMaxLength(500);
        entity.Property(e => e.Email).HasMaxLength(320);
        entity.Property(e => e.ContactPerson).HasMaxLength(200);
        entity.Property(e => e.Phone).HasMaxLength(80);

        entity.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasAlternateKey(e => new { e.OrganizationId, e.Id })
            .HasName("UX_Customers_Organization_Id");
    }

    private static void ConfigureJobWorkKinds(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobWorkKindRow>();

        entity.ToTable("JobWorkKinds");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .HasMaxLength(80);

        entity.Property(e => e.Label)
            .HasMaxLength(160)
            .IsRequired();

        entity.Property(e => e.IsActive)
            .HasDefaultValue(true);

        entity.Property(e => e.SortOrder)
            .HasDefaultValue(0);

        entity.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasIndex(e => e.Label)
            .IsUnique()
            .HasDatabaseName("UX_JobWorkKinds_Label");
    }

    private static void ConfigureJobClosureFlags(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobClosureFlagRow>();

        entity.ToTable("JobClosureFlags");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .HasMaxLength(80);

        entity.Property(e => e.Label)
            .HasMaxLength(160)
            .IsRequired();

        entity.Property(e => e.IsActive)
            .HasDefaultValue(true);

        entity.Property(e => e.SortOrder)
            .HasDefaultValue(0);

        entity.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasIndex(e => e.Label)
            .IsUnique()
            .HasDatabaseName("UX_JobClosureFlags_Label");
    }

    private static void ConfigureJobReports(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobReportRow>();

        entity.ToTable("JobReports", t =>
        {
            t.HasCheckConstraint("CK_JobReports_Status",
                "Status in ('Draft', 'Submitted', 'InReview', 'Approved', 'Rejected', 'Archived')");
        });
        entity.HasKey(e => e.Id);

        entity.Property(e => e.ReportNumber).HasMaxLength(80);

        entity.Property(e => e.Status)
            .HasMaxLength(40)
            .IsRequired();

        entity.Property(e => e.ReportDate).HasColumnType("date");
        entity.Property(e => e.TaskDescription).HasColumnType("nvarchar(max)");
        entity.Property(e => e.CustomerObservations).HasColumnType("nvarchar(max)");
        entity.Property(e => e.TechnicalObservations).HasColumnType("nvarchar(max)");

        entity.Property(e => e.WorkKind).HasMaxLength(80);
        entity.Property(e => e.CustomWorkKind).HasMaxLength(160);
        entity.Property(e => e.Remarks).HasColumnType("nvarchar(max)");

        entity.Property(e => e.ClosureFlagsJson)
            .HasColumnType("nvarchar(max)")
            .HasDefaultValueSql("'[]'")
            .IsRequired();

        entity.Property(e => e.CreatedAt).HasColumnType("datetimeoffset");
        entity.Property(e => e.UpdatedAt).HasColumnType("datetimeoffset");
        entity.Property(e => e.SubmittedAt).HasColumnType("datetimeoffset");
        entity.Property(e => e.DeletionScheduledAt).HasColumnType("datetimeoffset");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<CustomerRow>()
        .WithMany()
        .HasForeignKey(e => e.CustomerId)
        .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<JobWorkKindRow>()
            .WithMany()
            .HasForeignKey(e => e.WorkKind)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasAlternateKey(e => new { e.OrganizationId, e.Id })
            .HasName("UX_JobReports_Organization_Id");

        entity.HasIndex(e => new { e.OrganizationId, e.Status, e.UpdatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_JobReports_Organization_Status_UpdatedAt");

        entity.HasIndex(e => e.DeletionScheduledAt)
            .HasFilter("[DeletionScheduledAt] is not null")
            .HasDatabaseName("IX_JobReports_DeletionScheduledAt");
    }

    private static void ConfigureJobAssignments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobAssignmentRow>();

        entity.ToTable("JobAssignments");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.AssignedAt).HasColumnType("datetimeoffset");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<JobReportRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "ReportId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "UserId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "AssignedByUserId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.OrganizationId, e.ReportId, e.UserId })
            .IsUnique()
            .HasDatabaseName("UX_JobAssignments_Report_User");

        entity.HasIndex(e => new { e.OrganizationId, e.UserId })
            .HasDatabaseName("IX_JobAssignments_User");
    }

    private static void ConfigureJobReportLinks(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobReportLinkRow>();

        entity.ToTable("JobReportLinks", t =>
        {
            t.HasCheckConstraint("CK_JobReportLinks_NoSelfLink",
                "SourceReportId != TargetReportId");
        });
        entity.HasKey(e => e.Id);

        entity.Property(e => e.LinkType)
            .HasMaxLength(80)
            .IsRequired();

        entity.Property(e => e.CreatedAt).HasColumnType("datetimeoffset");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<JobReportRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "SourceReportId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<JobReportRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "TargetReportId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.OrganizationId, e.SourceReportId, e.TargetReportId })
            .IsUnique()
            .HasDatabaseName("UX_JobReportLinks_Pair");

        entity.HasIndex(e => new { e.OrganizationId, e.TargetReportId })
            .HasDatabaseName("IX_JobReportLinks_TargetReport");
    }

    private static void ConfigureJobEvents(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobEventRow>();

        entity.ToTable("JobEvents", t =>
        {
            t.HasCheckConstraint("CK_JobEvents_BeforeJson_IsJson",
                "BeforeJson is null or isjson(BeforeJson) = 1");
            t.HasCheckConstraint("CK_JobEvents_AfterJson_IsJson",
                "AfterJson is null or isjson(AfterJson) = 1");
        });
        entity.HasKey(e => e.Id);

        entity.Property(e => e.EventType)
            .HasMaxLength(80)
            .IsRequired();

        entity.Property(e => e.BeforeJson).HasColumnType("nvarchar(max)");
        entity.Property(e => e.AfterJson).HasColumnType("nvarchar(max)");

        entity.Property(e => e.CreatedAt).HasColumnType("datetimeoffset");

        entity.HasOne<OrganizationRow>()
           .WithMany()
           .HasForeignKey(e => e.OrganizationId)
           .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<JobReportRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "ReportId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey("OrganizationId", "ActorId")
            .HasPrincipalKey("OrganizationId", "Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.OrganizationId, e.ReportId, e.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_JobEvents_Report_CreatedAt");
    }

    private static void ConfigureInviteTokens(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<InviteTokenRow>();

        entity.ToTable("InviteTokens");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Email)
            .HasMaxLength(320)
            .IsRequired();

        entity.Property(e => e.Token)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(e => e.Role).HasMaxLength(80);

        entity.Property(e => e.ExpiresAt).HasColumnType("datetimeoffset");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => e.Token)
            .IsUnique()
            .HasDatabaseName("UX_InviteTokens_Token");

        entity.HasIndex(e => e.Email)
            .HasFilter("[Consumed] = 0")
            .HasDatabaseName("IX_InviteTokens_Email");
    }

    private static void ConfigureWorksheets(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorksheetRow>();

        entity.ToTable("Worksheets", t =>
        {
            t.HasCheckConstraint("CK_Worksheets_HoursWorked",
                "HoursWorked >= 0 and HoursWorked <= 24");
            t.HasCheckConstraint("CK_Worksheets_HoursWorked_Increment",
                "(HoursWorked * 4) % 1 = 0");
        });
        entity.HasKey(e => e.Id);

        entity.Property(e => e.WorkDate).HasColumnType("date");
        entity.Property(e => e.HoursWorked).HasColumnType("decimal(5,2)");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("sysutcdatetime()");

        entity.HasOne<OrganizationRow>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<JobReportRow>()
            .WithMany()
            .HasForeignKey("JobId")
            .HasPrincipalKey("Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey("UserId")
            .HasPrincipalKey("Id")
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.OrganizationId, e.Id })
            .IsUnique()
            .HasDatabaseName("UX_Worksheets_Organization_Id");

        entity.HasIndex(e => e.JobId)
            .HasDatabaseName("IX_Worksheets_JobId");

        entity.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_Worksheets_UserId");

        entity.HasIndex(e => e.WorkDate)
            .HasDatabaseName("IX_Worksheets_WorkDate");
    }

    private static void ConfigureInstallationTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstallationTypeRow>(entity =>
        {
            entity.ToTable("InstallationTypes", "dbo");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.IsActive)
                .HasDefaultValue(true);

            entity.Property(x => x.SortOrder)
                .HasDefaultValue(0);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne<OrganizationRow>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.JobReport)
                .WithMany(x => x.InstallationTypes)
                .HasForeignKey(x => new
                {
                    x.OrganizationId,
                    x.JobReportId
                })
                .HasPrincipalKey(x => new
                {
                    x.OrganizationId,
                    x.Id
                })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new
            {
                x.OrganizationId,
                x.JobReportId,
                x.Name
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.OrganizationId,
                x.JobReportId,
                x.SortOrder
            });
        });
    }
    private static void ConfigureControlCategory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ControlCategoryRow>(entity =>
        {
            entity.ToTable("ControlCategories", "dbo");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.SortOrder)
                .HasDefaultValue(0);

            entity.HasIndex(x => new
            {
                x.OrganizationId,
                x.Name
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.OrganizationId,
                x.SortOrder
            });
        });
    }

    private static void ConfigureControlPoint(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ControlPointRow>(entity =>
        {
            entity.ToTable("ControlPoints", "dbo");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.IsActive)
                .HasDefaultValue(true);

            entity.Property(x => x.IsChecked)
                .HasDefaultValue(false);

            entity.Property(x => x.SortOrder)
                .HasDefaultValue(0);

            entity.HasIndex(x => new
            {
                x.OrganizationId,
                x.Name
            });
        });
    }

    private static void ConfigureInstallationControlPoint(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstallationControlPointRow>(entity =>
        {
            entity.ToTable("InstallationControlPoints", "dbo");

            entity.HasKey(x => new
            {
                x.InstallationTypeId,
                x.ControlCategoryId,
                x.ControlPointId
            });

            entity.Property(x => x.SortOrder)
                .HasDefaultValue(0);

            entity.Property(x => x.IsRequired)
                .HasDefaultValue(false);

            entity.HasOne(x => x.InstallationType)
                .WithMany(x => x.ControlPoints)
                .HasForeignKey(x => x.InstallationTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ControlCategory)
                .WithMany(x => x.InstallationControlPoints)
                .HasForeignKey(x => x.ControlCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ControlPoint)
                .WithMany(x => x.InstallationTypes)
                .HasForeignKey(x => x.ControlPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.InstallationTypeId,
                x.ControlCategoryId,
                x.SortOrder
            });

            entity.HasIndex(x => new
            {
                x.ControlCategoryId,
                x.ControlPointId
            });
        });
    }

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
            .WithMany(d => d.Mappings)
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
}
