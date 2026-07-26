using Microsoft.EntityFrameworkCore;

using Workslip.Domain.Models;



namespace Workslip.Infrastructure.Schema;



public sealed class SqlDbContext : DbContext

{

    public bool IsSeeding { get; set; }



    public SqlDbContext(DbContextOptions<SqlDbContext> options) : base(options)

    {

    }



    public DbSet<OrganizationRow> Organizations => Set<OrganizationRow>();

    public DbSet<UserDataRow> Users => Set<UserDataRow>();

    public DbSet<CustomerRow> Customers => Set<CustomerRow>();

    public DbSet<JobWorkKindRow> JobWorkKinds => Set<JobWorkKindRow>();

    public DbSet<JobClosureFlagRow> JobClosureFlags => Set<JobClosureFlagRow>();

    public DbSet<JobReportClosureFlagRow> JobReportClosureFlags => Set<JobReportClosureFlagRow>();

    public DbSet<JobReportRow> JobReports => Set<JobReportRow>();

    public DbSet<JobAssignmentRow> JobAssignments => Set<JobAssignmentRow>();

    public DbSet<JobReportLinkRow> JobReportLinks => Set<JobReportLinkRow>();

    public DbSet<JobEventRow> JobEvents => Set<JobEventRow>();

    public DbSet<InviteTokenRow> InviteTokens => Set<InviteTokenRow>();

    public DbSet<WorksheetRow> Worksheets => Set<WorksheetRow>();



    public DbSet<PushSubscriptionRow> PushSubscriptions => Set<PushSubscriptionRow>();

    public DbSet<NotificationQueueRow> NotificationQueue => Set<NotificationQueueRow>();

    public DbSet<NotificationDeliveryLogRow> NotificationDeliveryLog => Set<NotificationDeliveryLogRow>();

    public DbSet<JobViewRow> JobViews => Set<JobViewRow>();
    public DbSet<IdempotencyRecordRow> IdempotencyRecords => Set<IdempotencyRecordRow>();



    public DbSet<JobReportInstallationRow> JobReportInstallations => Set<JobReportInstallationRow>();

    public DbSet<JobReportInstallationCategoryRow> JobReportInstallationCategories => Set<JobReportInstallationCategoryRow>();

    public DbSet<JobReportInstallationControlPointRow> JobReportInstallationControlPoints => Set<JobReportInstallationControlPointRow>();

    public DbSet<ControlPointRow> ControlPointRow => Set<ControlPointRow>();

    public DbSet<ControlCategoryRow> ControlCategoryRow => Set<ControlCategoryRow>();

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

        ConfigureJobReportClosureFlags(modelBuilder);

        ConfigureJobReports(modelBuilder);

        ConfigureJobAssignments(modelBuilder);

        ConfigureJobReportLinks(modelBuilder);

        ConfigureJobEvents(modelBuilder);

        ConfigureInviteTokens(modelBuilder);

        ConfigureWorksheets(modelBuilder);

        ConfigureJobReportInstallations(modelBuilder);

        ConfigureControlCategory(modelBuilder);

        ConfigureControlPoint(modelBuilder);

        ConfigureJobReportInstallationCategories(modelBuilder);

        ConfigureJobReportInstallationControlPoints(modelBuilder);

        ConfigureInstallationTypeDefinitions(modelBuilder);

        ConfigureInstallationTypeDefinitionMappings(modelBuilder);

        ConfigurePushSubscriptions(modelBuilder);

        ConfigureNotificationQueue(modelBuilder);

        ConfigureNotificationDeliveryLog(modelBuilder);
        ConfigureJobViews(modelBuilder);
        ConfigureIdempotencyRecords(modelBuilder);
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

        entity.Property(e => e.IsFavorite).HasDefaultValue(false);



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



        entity.Property(e => e.NormalizedLabel)

            .HasMaxLength(80);



        entity.Property(e => e.Label)

            .HasMaxLength(160)

            .IsRequired();



        entity.Property(e => e.IsActive)

            .HasDefaultValue(true);



        entity.Property(e => e.SortOrder)

            .HasDefaultValue(0);



        entity.HasIndex(e => e.Label)

            .IsUnique()

            .HasDatabaseName("UX_JobWorkKinds_Label");

    }



    private static void ConfigureJobClosureFlags(ModelBuilder modelBuilder)

    {

        var entity = modelBuilder.Entity<JobClosureFlagRow>();



        entity.ToTable("JobClosureFlags");

        entity.HasKey(e => e.Id);



        entity.Property(e => e.NormalizedLabel)

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



    private static void ConfigureJobReportClosureFlags(ModelBuilder modelBuilder)

    {

        var entity = modelBuilder.Entity<JobReportClosureFlagRow>();



        entity.ToTable("JobReportClosureFlags");

        entity.HasKey(e => e.Id);



        entity.Property(e => e.SortOrder).HasDefaultValue(0);



        entity.HasOne(e => e.ClosureFlag)

            .WithMany()

            .HasForeignKey(e => e.ClosureFlagId)

            .OnDelete(DeleteBehavior.Restrict);



        entity.HasOne(e => e.JobReport)

            .WithMany(j => j.ClosureFlags)

            .HasForeignKey(e => e.JobReportId)

            .OnDelete(DeleteBehavior.Cascade);



        entity.HasOne<OrganizationRow>()

            .WithMany()

            .HasForeignKey(e => e.OrganizationId)

            .OnDelete(DeleteBehavior.Restrict);



        entity.HasIndex(e => new { e.JobReportId, e.ClosureFlagId })

            .IsUnique()

            .HasDatabaseName("UX_JobReportClosureFlags_Report_Flag");

    }



    private static void ConfigureJobReports(ModelBuilder modelBuilder)

    {

        var entity = modelBuilder.Entity<JobReportRow>();



        entity.ToTable("JobReports", t =>

        {

            t.HasCheckConstraint("CK_JobReports_Status",

                "Status in ('Draft', 'InReview', 'Approved', 'Rejected')");

        });

        entity.HasKey(e => e.Id);



        entity.Property(e => e.ReportNumber).HasMaxLength(80);

entity.Property(e => e.Status)

            .HasMaxLength(40)

            .IsRequired();


        entity.Property(e => e.JobType).HasColumnType("nvarchar(max)");


        entity.Property(e => e.ReportDate).HasColumnType("date");

        entity.Property(e => e.TaskDescription).HasColumnType("nvarchar(max)");

        entity.Property(e => e.CustomerObservations).HasColumnType("nvarchar(max)");

        entity.Property(e => e.TechnicalObservations).HasColumnType("nvarchar(max)");



        entity.Property(e => e.CustomWorkKind).HasMaxLength(250);

        entity.Property(e => e.Remarks).HasColumnType("nvarchar(max)");



        entity.Property(e => e.CustomerName);

        entity.Property(e => e.CustomerEmail);

        entity.Property(e => e.CustomerPhone);

        entity.Property(e => e.CustomerAddress);

        entity.Property(e => e.DestinationAddress);

        entity.Property(e => e.DestinationZipCode).HasMaxLength(10);

        entity.Property(e => e.DestinationCity).HasMaxLength(200);



        entity.Property(e => e.CreatedAt).HasColumnType("datetimeoffset");

        entity.Property(e => e.UpdatedAt).HasColumnType("datetimeoffset");

        entity.Property(e => e.SubmittedAt).HasColumnType("datetimeoffset");

        entity.Property(e => e.DeletionScheduledAt).HasColumnType("datetimeoffset");

        entity.Property(e => e.RejectionNote).HasColumnType("nvarchar(max)");


        entity.HasOne(x => x.OrganizationRow)

            .WithMany()

            .HasForeignKey(e => e.OrganizationId)

            .OnDelete(DeleteBehavior.Restrict);



        entity.HasOne(x => x.CustomerRow)

        .WithMany()

        .HasForeignKey(e => new { e.OrganizationId, e.CustomerId })

        .HasPrincipalKey(e => new { e.OrganizationId, e.Id })

        .OnDelete(DeleteBehavior.Restrict);



        entity.HasOne(x => x.WorkKindRow)

            .WithMany()

            .HasForeignKey(e => e.WorkKindId)

            .OnDelete(DeleteBehavior.Restrict);



        entity.HasIndex(e => new { e.OrganizationId, e.Status, e.UpdatedAt })

            .IsDescending(false, false, true)

            .HasDatabaseName("IX_JobReports_Organization_Status_UpdatedAt");



        entity.HasIndex(e => new { e.OrganizationId, e.ReportNumber })

            .IsUnique()

            .HasDatabaseName("UX_JobReports_Organization_ReportNumber");



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



        entity.Property(e => e.Summary).HasMaxLength(500);



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



        entity.Property(e => e.OpenedAt)

            .HasColumnType("datetimeoffset");



        entity.Property(e => e.AcceptedAt)

            .HasColumnType("datetimeoffset");



        entity.Property(e => e.EntraUserId)

            .HasMaxLength(80);



        entity.Property(e => e.EntraEmail)

            .HasMaxLength(320);



        entity.Property(e => e.EntraProvisionedAt)

            .HasColumnType("datetimeoffset");



        entity.Property(e => e.EntraCleanedAt)

            .HasColumnType("datetimeoffset");



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

            .HasForeignKey(e => new { e.OrganizationId, e.JobId })

            .HasPrincipalKey(e => new { e.OrganizationId, e.Id })

            .OnDelete(DeleteBehavior.Restrict);



        entity.HasOne<UserDataRow>()

            .WithMany()

            .HasForeignKey(e => new { e.OrganizationId, e.UserId })

            .HasPrincipalKey(e => new { e.OrganizationId, e.Id })

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

            .HasForeignKey(x => new { x.OrganizationId, x.InstallationTypeDefinitionId })

            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })

            .OnDelete(DeleteBehavior.Restrict);



        entity.HasIndex(x => new { x.OrganizationId, x.JobReportId, x.InstallationTypeDefinitionId })

            .IsUnique();



        entity.HasIndex(x => new { x.OrganizationId, x.JobReportId, x.SortOrder });

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



            entity.Property(x => x.IsActive)

                .HasDefaultValue(true);



            entity.Property(x => x.SortOrder)

                .HasDefaultValue(0);



            entity.HasIndex(x => new

            {

                x.OrganizationId,

                x.Name

            });

        });

    }



    private static void ConfigureJobReportInstallationCategories(ModelBuilder modelBuilder)

    {

        var entity = modelBuilder.Entity<JobReportInstallationCategoryRow>();



        entity.ToTable("JobReportInstallationCategories", "dbo");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.SortOrder)

            .HasDefaultValue(0);

        entity.Property(x => x.IsIrrelevant)

            .HasDefaultValue(false);

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



        entity.Property(x => x.IsChecked)

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



        entity.HasAlternateKey(e => new { e.OrganizationId, e.Id });



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

    private static void ConfigurePushSubscriptions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PushSubscriptionRow>();

        entity.ToTable("PushSubscriptions");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Endpoint).HasMaxLength(2000).IsRequired();
        entity.Property(e => e.P256Dh).HasMaxLength(200).IsRequired();
        entity.Property(e => e.Auth).HasMaxLength(200).IsRequired();
        entity.Property(e => e.UserAgent).HasMaxLength(500);
        entity.Property(e => e.IsActive).HasDefaultValue(true);
        entity.Property(e => e.CreatedUtc).HasColumnType("datetimeoffset").HasDefaultValueSql("sysutcdatetime()");
        entity.Property(e => e.LastSeenUtc).HasColumnType("datetimeoffset").HasDefaultValueSql("sysutcdatetime()");

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.UserId, e.IsActive }).HasDatabaseName("IX_PushSubscriptions_User_Active");
    }

    private static void ConfigureNotificationQueue(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NotificationQueueRow>();

        entity.ToTable("NotificationQueue");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.NotificationType).HasMaxLength(100).IsRequired();
        entity.Property(e => e.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
        entity.Property(e => e.RetryCount).HasDefaultValue(0);
        entity.Property(e => e.CreatedUtc).HasColumnType("datetimeoffset").HasDefaultValueSql("sysutcdatetime()");
        entity.Property(e => e.ProcessingStartedUtc).HasColumnType("datetimeoffset");
        entity.Property(e => e.NextAttemptUtc).HasColumnType("datetimeoffset");
        entity.Property(e => e.CompletedUtc).HasColumnType("datetimeoffset");
        entity.Property(e => e.ReadUtc).HasColumnType("datetimeoffset");
        entity.Property(e => e.LastError).HasColumnType("nvarchar(max)");

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.Status, e.NextAttemptUtc }).HasDatabaseName("IX_NotificationQueue_Status_NextAttempt");
    }

    private static void ConfigureNotificationDeliveryLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NotificationDeliveryLogRow>();

        entity.ToTable("NotificationDeliveryLog");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.ErrorMessage).HasColumnType("nvarchar(max)");
        entity.Property(e => e.SentUtc).HasColumnType("datetimeoffset").HasDefaultValueSql("sysutcdatetime()");

        entity.HasOne<PushSubscriptionRow>()
            .WithMany()
            .HasForeignKey(e => e.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureJobViews(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobViewRow>();

        entity.ToTable("JobViews");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.ViewType).HasMaxLength(50).IsRequired();
        entity.Property(e => e.ViewedAt).HasColumnType("datetimeoffset");

        entity.HasIndex(e => new { e.JobId, e.UserId, e.ViewType }).IsUnique();

        entity.HasOne<JobReportRow>()
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<UserDataRow>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureIdempotencyRecords(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdempotencyRecordRow>();
        entity.ToTable("IdempotencyRecords");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Scope).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ReservationToken).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.CreatedAt).HasColumnType("datetimeoffset");
        entity.Property(x => x.ExpiresAt).HasColumnType("datetimeoffset");
        entity.HasIndex(x => new { x.Scope, x.Key }).IsUnique().HasDatabaseName("UX_IdempotencyRecords_Scope_Key");
        entity.HasIndex(x => x.ExpiresAt).HasDatabaseName("IX_IdempotencyRecords_ExpiresAt");
    }


}
