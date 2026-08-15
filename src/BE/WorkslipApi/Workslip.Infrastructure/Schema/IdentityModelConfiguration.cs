using Microsoft.EntityFrameworkCore;

using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

internal static class IdentityModelConfiguration
{
    internal static void ConfigureOrganizations(ModelBuilder modelBuilder)

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



    internal static void ConfigureUsers(ModelBuilder modelBuilder)

    {

        var entity = modelBuilder.Entity<UserDataRow>();



        entity.ToTable("Users", table => table.UseSqlOutputClause(false));

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



}
