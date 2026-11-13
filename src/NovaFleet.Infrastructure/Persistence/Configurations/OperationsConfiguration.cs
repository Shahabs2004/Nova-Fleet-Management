using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaFleet.Domain.Auditing;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Sessions;

namespace NovaFleet.Infrastructure.Persistence.Configurations;

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SessionKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LoginMethod).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ClientIpAddress).HasMaxLength(64);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => x.SessionKey).IsUnique();
        builder.HasIndex(x => new { x.Username, x.LoginAtUtc });
        builder.HasIndex(x => new { x.Status, x.LastActivityAtUtc });
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorUsername).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AfterJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ClientIpAddress).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.ActorUsername);
    }
}

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("UserNotifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RecipientUsername).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(100);
        builder.Property(x => x.ReferenceId).HasMaxLength(100);
        builder.HasIndex(x => new { x.RecipientUsername, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.RecipientUsername, x.ReadAtUtc });
    }
}

public sealed class SmsQueueItemConfiguration : IEntityTypeConfiguration<SmsQueueItem>
{
    public void Configure(EntityTypeBuilder<SmsQueueItem> builder)
    {
        builder.ToTable("SmsQueueItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasConversion<int>();
        builder.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RecipientMobile).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RecipientName).HasMaxLength(200);
        builder.Property(x => x.MessageBody).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ProviderReference).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.CanRetryManually);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => x.MissionId);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

public sealed class SmsTemplateConfiguration : IEntityTypeConfiguration<SmsTemplate>
{
    public void Configure(EntityTypeBuilder<SmsTemplate> builder)
    {
        builder.ToTable("SmsTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasConversion<int>();
        builder.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.EventKey).IsUnique();
    }
}

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).HasColumnType("nvarchar(max)");
        builder.Property(x => x.IsSecret).HasDefaultValue(false);
        builder.Property(x => x.ProtectedValue).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Key).IsUnique();
    }
}
