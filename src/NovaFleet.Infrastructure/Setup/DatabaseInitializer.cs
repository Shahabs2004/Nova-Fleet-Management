using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Setup;

/// <summary>اعمال Migrationها و seed مقادیر پایه؛ اجرای چندباره اثری ندارد.</summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(NovaFleetDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await SeedAsync(db, cancellationToken);
    }

    public static async Task SeedAsync(NovaFleetDbContext db, CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(db, cancellationToken);
        await SeedSplitReasonsAsync(db, cancellationToken);
        await SeedSmsTemplatesAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// نقش‌های پایه مطابق پیوست ب SPEC. نقش سیستمی حذف یا تغییر نام نمی‌شود و
    /// فقط در صورت نبود ساخته می‌شود تا تغییرات مدیر سامانه از بین نرود.
    /// </summary>
    private static async Task SeedRolesAsync(NovaFleetDbContext db, CancellationToken cancellationToken)
    {
        var templates = new (string Name, string Title, string Description, bool System, string[] Permissions)[]
        {
            ("SystemAdministrator", "مدیر سامانه", "دسترسی کامل به همه بخش‌ها", true,
                ["*"]),
            ("FleetManager", "مدیر ناوگان", "مدیریت کامل مأموریت‌ها، رانندگان و خودروها و گزارش‌ها", true,
                [
                    PermissionCatalog.Dashboard.View,
                    PermissionCatalog.Drivers.All,
                    PermissionCatalog.Drivers.DefaultVehicleManage,
                    PermissionCatalog.Vehicles.All,
                    PermissionCatalog.Missions.All,
                    PermissionCatalog.Missions.SplitScheduleEdit,
                    PermissionCatalog.Missions.WithdrawalReview,
                    PermissionCatalog.Reports.View,
                    PermissionCatalog.Reports.Export,
                    PermissionCatalog.Notifications.View
                ]),
            ("Operator", "اپراتور", "بررسی، تأیید و اجرای مأموریت‌ها", true,
                [
                    PermissionCatalog.Dashboard.View,
                    PermissionCatalog.Drivers.View,
                    PermissionCatalog.Vehicles.View,
                    PermissionCatalog.Missions.View,
                    PermissionCatalog.Missions.ViewOwn,
                    PermissionCatalog.Missions.Create,
                    PermissionCatalog.Missions.Edit,
                    PermissionCatalog.Missions.Review,
                    PermissionCatalog.Missions.Approve,
                    PermissionCatalog.Missions.Reject,
                    PermissionCatalog.Missions.Cancel,
                    PermissionCatalog.Missions.Extend,
                    PermissionCatalog.Missions.Split,
                    PermissionCatalog.Missions.Print,
                    PermissionCatalog.Missions.TimelineView,
                    PermissionCatalog.Missions.OdometerManage,
                    PermissionCatalog.Missions.ChangeReview,
                    PermissionCatalog.Notifications.View
                ]),
            ("Requester", "درخواست‌کننده", "ثبت و پیگیری درخواست مأموریت", true,
                [
                    PermissionCatalog.Dashboard.View,
                    PermissionCatalog.Missions.ViewOwn,
                    PermissionCatalog.Missions.Create,
                    PermissionCatalog.Missions.Edit,
                    PermissionCatalog.Missions.Print,
                    PermissionCatalog.Notifications.View
                ]),
            ("Viewer", "ناظر", "مشاهده اطلاعات ناوگان", true,
                [
                    PermissionCatalog.Dashboard.View,
                    PermissionCatalog.Drivers.View,
                    PermissionCatalog.Vehicles.View,
                    PermissionCatalog.Missions.View
                ])
        };

        foreach (var template in templates)
        {
            var role = await db.Roles.FirstOrDefaultAsync(item => item.Name == template.Name, cancellationToken);
            if (role is null)
            {
                role = new Role(template.Name, template.Title, template.Description, template.System);
                db.Roles.Add(role);
                await db.SaveChangesAsync(cancellationToken);
            }

            var hasPermissions = await db.RolePermissions.AnyAsync(item => item.RoleId == role.Id, cancellationToken);
            if (!hasPermissions)
            {
                db.RolePermissions.AddRange(template.Permissions
                    .Select(permission => new RolePermission(role.Id, permission, PermissionEffect.Grant)));
            }
        }
    }

    private static async Task SeedSplitReasonsAsync(NovaFleetDbContext db, CancellationToken cancellationToken)
    {
        if (await db.MissionSplitReasons.AnyAsync(cancellationToken))
        {
            return;
        }

        var defaults = new[]
        {
            ("خرابی خودرو", 1),
            ("تعویض راننده", 2),
            ("تعویض هم‌زمان راننده و خودرو", 3),
            ("سایر", 4)
        };

        db.MissionSplitReasons.AddRange(defaults.Select(item => new MissionSplitReason(item.Item1, item.Item2)));
    }

    private static async Task SeedSmsTemplatesAsync(NovaFleetDbContext db, CancellationToken cancellationToken)
    {
        var events = new[]
        {
            SmsEventType.MissionApproved,
            SmsEventType.MissionRejected,
            SmsEventType.MissionChanged,
            SmsEventType.DriverOrVehicleChanged,
            SmsEventType.MissionCancelled,
            SmsEventType.WithdrawalApproved,
            SmsEventType.WithdrawalRejected,
            SmsEventType.MissionStartReminder,
            SmsEventType.MissionCompleted
        };

        var existing = await db.SmsTemplates
            .Select(item => item.EventKey)
            .ToListAsync(cancellationToken);

        foreach (var eventType in events)
        {
            var eventKey = SmsEventNames.Of(eventType);
            if (existing.Contains(eventKey))
            {
                continue;
            }

            db.SmsTemplates.Add(new SmsTemplate(eventType, Application.Notifications.SmsDefaults.BodyFor(eventType), true));
        }
    }
}
