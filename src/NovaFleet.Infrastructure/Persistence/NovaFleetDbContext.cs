using Microsoft.EntityFrameworkCore;
using NovaFleet.Domain.Auditing;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Locations;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Organization;
using NovaFleet.Domain.Sessions;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.Infrastructure.Persistence;

/// <summary>پایگاه داده واحد سامانه؛ هر ماژول مجموعه‌های خود را از همین Context می‌خواند.</summary>
public sealed class NovaFleetDbContext(DbContextOptions<NovaFleetDbContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<DirectoryGroupRoleMapping> DirectoryGroupRoleMappings => Set<DirectoryGroupRoleMapping>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<MissionChangeRequest> MissionChangeRequests => Set<MissionChangeRequest>();
    public DbSet<MissionSplitReason> MissionSplitReasons => Set<MissionSplitReason>();
    public DbSet<MissionNumberSequence> MissionNumberSequences => Set<MissionNumberSequence>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<SmsQueueItem> SmsQueueItems => Set<SmsQueueItem>();
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NovaFleetDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
