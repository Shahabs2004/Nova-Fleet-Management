using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Missions;

namespace NovaFleet.Infrastructure.Persistence.Configurations;

public sealed class MissionConfiguration : IEntityTypeConfiguration<Mission>
{
    public void Configure(EntityTypeBuilder<Mission> builder)
    {
        builder.ToTable("Missions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MissionNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequesterUsername).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RequesterName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequesterPhoneSnapshot).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequesterOrganizationalUnit).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestingGroup).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Origin).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Destination).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RouteJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.MissionOrderNumber).HasMaxLength(50);
        builder.Property(x => x.MissionOrderDate);
        builder.Property(x => x.CargoDescription).HasMaxLength(500);
        builder.Property(x => x.ConsigneeName).HasMaxLength(200);
        builder.Property(x => x.PassengerNames).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ApprovedBy).HasMaxLength(100);
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.Property(x => x.StatusBeforeWithdrawal).HasConversion<int>();
        builder.Property(x => x.SplitReason).HasMaxLength(500);
        builder.Property(x => x.SplitBy).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.IsActive);
        builder.Ignore(x => x.IsFinal);
        builder.Ignore(x => x.DistanceKm);
        builder.HasIndex(x => x.MissionNumber).IsUnique();
        builder.HasIndex(x => new { x.Status, x.PlannedStartAtUtc });
        builder.HasIndex(x => new { x.DriverId, x.PlannedStartAtUtc });
        builder.HasIndex(x => new { x.VehicleId, x.PlannedStartAtUtc });
        builder.HasIndex(x => x.RequesterUsername);
    }
}

public sealed class MissionChangeRequestConfiguration : IEntityTypeConfiguration<MissionChangeRequest>
{
    public void Configure(EntityTypeBuilder<MissionChangeRequest> builder)
    {
        builder.ToTable("MissionChangeRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequesterUsername).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ProposedValuesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ReviewerUsername).HasMaxLength(100);
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.HasOne<Mission>().WithMany().HasForeignKey(x => x.MissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.MissionId, x.Status });
    }
}

public sealed class MissionSplitReasonConfiguration : IEntityTypeConfiguration<MissionSplitReason>
{
    public void Configure(EntityTypeBuilder<MissionSplitReason> builder)
    {
        builder.ToTable("MissionSplitReasons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Title).IsUnique();
    }
}

public sealed class MissionNumberSequenceConfiguration : IEntityTypeConfiguration<MissionNumberSequence>
{
    public void Configure(EntityTypeBuilder<MissionNumberSequence> builder)
    {
        builder.ToTable("MissionNumberSequences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Year).IsUnique();
    }
}
