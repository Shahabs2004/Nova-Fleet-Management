using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaFleet.Domain.Maintenance;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.Infrastructure.Persistence.Configurations;

public sealed class MaintenanceConfiguration : IEntityTypeConfiguration<MaintenanceTemplate>,
    IEntityTypeConfiguration<MaintenanceRecord>, IEntityTypeConfiguration<MaintenanceRecordItem>,
    IEntityTypeConfiguration<MaintenanceRecordPart>, IEntityTypeConfiguration<MaintenanceSchedule>,
    IEntityTypeConfiguration<VehicleOdometerReading>, IEntityTypeConfiguration<MaintenanceRevision>,
    IEntityTypeConfiguration<MaintenanceAttachment>, IEntityTypeConfiguration<MaintenanceReminderDelivery>
{
    public void Configure(EntityTypeBuilder<MaintenanceTemplate> b)
    {
        b.ToTable("MaintenanceTemplates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100).IsRequired();
        b.Property(x => x.AssetKey).HasMaxLength(40).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Name).IsUnique();
    }

    public void Configure(EntityTypeBuilder<MaintenanceRecord> b)
    {
        b.ToTable("MaintenanceRecords");
        b.HasKey(x => x.Id);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Workshop).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.OtherCostToman).HasPrecision(18, 0);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.VehicleId, x.PerformedAtUtc });
        b.HasIndex(x => new { x.VehicleId, x.RequestId }).IsUnique();
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<MaintenanceRecordItem> b)
    {
        b.ToTable("MaintenanceRecordItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100).IsRequired();
        b.Property(x => x.AssetKey).HasMaxLength(40).IsRequired();
        b.Property(x => x.LaborCostToman).HasPrecision(18, 0);
        b.HasOne<MaintenanceTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaintenanceSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Parts).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<MaintenanceRecordPart> b)
    {
        b.ToTable("MaintenanceRecordParts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Specification).HasMaxLength(500);
        b.Property(x => x.UnitCostToman).HasPrecision(18, 0);
    }

    public void Configure(EntityTypeBuilder<MaintenanceSchedule> b)
    {
        b.ToTable("MaintenanceSchedules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaintenanceTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.VehicleId, x.TemplateId }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<VehicleOdometerReading> b)
    {
        b.ToTable("VehicleOdometerReadings");
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceKind).HasMaxLength(24).IsRequired();
        b.Property(x => x.RecordedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SourceKind, x.SourceId }).IsUnique();
        b.HasIndex(x => new { x.VehicleId, x.ObservedAtUtc });
    }

    public void Configure(EntityTypeBuilder<MaintenanceRevision> b)
    {
        b.ToTable("MaintenanceRevisions");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityKind).HasMaxLength(24).IsRequired();
        b.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.EntityKind, x.EntityId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.VehicleId, x.CreatedAtUtc });
    }

    public void Configure(EntityTypeBuilder<MaintenanceAttachment> b)
    {
        b.ToTable("MaintenanceAttachments");
        b.HasKey(x => x.Id);
        b.HasOne<MaintenanceRecord>().WithMany().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired();
        b.Property(x => x.UploadedBy).HasMaxLength(100).IsRequired();
    }

    public void Configure(EntityTypeBuilder<MaintenanceReminderDelivery> b)
    {
        b.ToTable("MaintenanceReminderDeliveries");
        b.HasKey(x => x.Id);
        b.HasOne<MaintenanceSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CycleKey).HasMaxLength(80).IsRequired();
        b.Property(x => x.Stage).HasMaxLength(20).IsRequired();
        b.Property(x => x.Recipient).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ScheduleId, x.CycleKey, x.Stage, x.Recipient }).IsUnique();
    }
}
