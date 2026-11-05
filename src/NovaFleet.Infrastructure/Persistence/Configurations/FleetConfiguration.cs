using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Locations;
using NovaFleet.Domain.Organization;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.Infrastructure.Persistence.Configurations;

public sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MobilePhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EmploymentStatus).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.DisplayName);
        builder.HasIndex(x => new { x.LastName, x.FirstName });
        builder.HasIndex(x => x.MobilePhone);
    }
}

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Brand).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlateFirstTwo).HasMaxLength(5).IsRequired();
        builder.Property(x => x.PlateLetter).HasMaxLength(5).IsRequired();
        builder.Property(x => x.PlateNextThree).HasMaxLength(5).IsRequired();
        builder.Property(x => x.PlateIranCode).HasMaxLength(5).IsRequired();
        builder.Property(x => x.ManualStatus).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.LicensePlate);
        builder.Ignore(x => x.PlateDisplay);
        builder.Ignore(x => x.DisplayName);
        builder.HasIndex(x => new { x.PlateFirstTwo, x.PlateLetter, x.PlateNextThree, x.PlateIranCode }).IsUnique();
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ProposedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReviewedBy).HasMaxLength(100);
        builder.Property(x => x.ReviewReason).HasMaxLength(500);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}

public sealed class OrganizationUnitConfiguration : IEntityTypeConfiguration<OrganizationUnit>
{
    public void Configure(EntityTypeBuilder<OrganizationUnit> builder)
    {
        builder.ToTable("OrganizationUnits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
