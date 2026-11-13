namespace NovaFleet.Application.Authorization;

/// <summary>
/// تعریف رسمی مجوزهای قابل استفاده؛ کلید خارج از این کاتالوگ معتبر نیست.
/// مجوزهای ماژول با wildcard مانند <c>drivers.*</c> قابل اعطا هستند اما مجوزهای مدیریتی
/// عمداً بیرون از فضای نام ماژول قرار گرفته‌اند تا با wildcard اعطا نشوند.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>مجوز فراگیر همه عملیات؛ فقط برای مدیر سامانه و مدیر محلی.</summary>
    public const string All = "*";

    public static class Dashboard
    {
        public const string View = "dashboard.view";
    }

    public static class Drivers
    {
        public const string View = "drivers.view";
        public const string Manage = "drivers.manage";
        public const string All = "drivers.*";
        public const string DefaultVehicleManage = "drivers.default-vehicle.manage";
    }

    public static class Vehicles
    {
        public const string View = "vehicles.view";
        public const string Manage = "vehicles.manage";
        public const string All = "vehicles.*";
    }

    public static class Missions
    {
        public const string View = "missions.view";
        public const string ViewOwn = "missions.view.own";
        public const string Create = "missions.create";
        public const string Edit = "missions.edit";
        public const string Review = "missions.review";
        public const string Approve = "missions.approve";
        public const string Reject = "missions.reject";
        public const string Cancel = "missions.cancel";
        public const string Extend = "missions.extend";
        public const string Split = "missions.split";
        public const string Print = "missions.print";
        public const string TimelineView = "missions.timeline.view";
        public const string All = "missions.*";
        public const string OdometerManage = "missions.odometer.manage";
        public const string SplitScheduleEdit = "missions.split.schedule.edit";
        public const string WithdrawalReview = "missions.withdrawal.review";
        public const string ChangeReview = "missions.change.review";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    public static class Notifications
    {
        public const string View = "notifications.view";
        public const string SmsView = "notifications.sms.view";
        public const string SmsRetry = "notifications.sms.retry";
        public const string SmsTemplatesManage = "notifications.sms.templates.manage";
    }

    public static class Security
    {
        public const string AuditView = "security.audit.view";
        public const string SessionsView = "security.sessions.view";
    }

    public static class Administration
    {
        public const string AuthorizationManage = "settings.authorization";
        public const string ConnectionsManage = "settings.connections.manage";
        public const string SessionsManage = "settings.sessions";
        public const string SystemManage = "settings.system.manage";
        public const string UsersManage = "users.manage";
        public const string LocationsManage = "locations.manage";
        public const string MissionTemplateManage = "settings.mission-template.manage";
        public const string MissionSplitReasonsManage = "settings.mission-split-reasons.manage";
    }

    private static readonly IReadOnlyList<PermissionDefinition> DefinitionsValue =
        Array.AsReadOnly<PermissionDefinition>(
        [
            new(All, "همه مجوزها", "مجوز فراگیر؛ فقط برای نقش مدیر سامانه و مدیر محلی."),
            new(Dashboard.View, "مشاهده داشبورد", "مشاهده داشبورد متناسب با نقش و سطح دسترسی."),
            new(Drivers.View, "مشاهده رانندگان", "مشاهده فهرست و جزئیات رانندگان."),
            new(Drivers.Manage, "مدیریت رانندگان", "افزودن، ویرایش و بایگانی رانندگان."),
            new(Drivers.All, "همه عملیات رانندگان", "الگوی فراگیر مجوزهای ماژول رانندگان."),
            new(Drivers.DefaultVehicleManage, "مدیریت خودروی پیش‌فرض راننده", "تغییر خودروی پیش‌فرض پرونده راننده."),
            new(Vehicles.View, "مشاهده خودروها", "مشاهده فهرست و جزئیات خودروها."),
            new(Vehicles.Manage, "مدیریت خودروها", "افزودن، ویرایش و بایگانی خودروها."),
            new(Vehicles.All, "همه عملیات خودروها", "الگوی فراگیر مجوزهای ماژول خودروها."),
            new(Missions.View, "مشاهده همه مأموریت‌ها", "مشاهده مأموریت‌های مجاز."),
            new(Missions.ViewOwn, "مشاهده مأموریت‌های خود", "مشاهده درخواست‌ها و مأموریت‌های ثبت‌شده توسط کاربر."),
            new(Missions.Create, "ثبت مأموریت", "ثبت درخواست یا مأموریت مطابق گردش‌کار."),
            new(Missions.Edit, "ویرایش مأموریت", "ویرایش اطلاعات مأموریت در محدوده مجاز."),
            new(Missions.Review, "شروع بررسی", "شروع بررسی درخواست‌ها و قفل‌کردن مورد در حال بررسی."),
            new(Missions.Approve, "تأیید مأموریت", "تأیید درخواست و تخصیص منابع."),
            new(Missions.Reject, "رد مأموریت", "رد درخواست با ثبت علت."),
            new(Missions.Cancel, "لغو مأموریت", "لغو مدیریتی مأموریت با ثبت علت."),
            new(Missions.Extend, "تمدید مأموریت", "تمدید زمان مأموریت با کنترل تداخل."),
            new(Missions.Split, "تقسیم مأموریت", "تقسیم مأموریت در حال اجرا به دو مأموریت مستقل."),
            new(Missions.Print, "چاپ حکم مأموریت", "مشاهده و چاپ حکم مأموریت."),
            new(Missions.TimelineView, "مشاهده تایم‌لاین", "مشاهده تایم‌لاین مأموریت‌ها."),
            new(Missions.All, "همه عملیات مأموریت", "الگوی فراگیر مجوزهای ماژول مأموریت."),
            new(Missions.OdometerManage, "ثبت و اصلاح کیلومتر", "ثبت یا اصلاح کیلومتر شروع و پایان."),
            new(Missions.SplitScheduleEdit, "ویرایش زمان تقسیم", "ویرایش زمان تقسیم مأموریت؛ فقط مدیر ناوگان."),
            new(Missions.WithdrawalReview, "بررسی انصراف", "تأیید یا رد درخواست انصراف از مأموریت."),
            new(Missions.ChangeReview, "بررسی درخواست تغییر", "بررسی، تأیید یا رد درخواست تغییر مأموریت."),
            new(Administration.MissionTemplateManage, "مدیریت قالب حکم", "مدیریت قالب چاپ حکم مأموریت."),
            new(Administration.MissionSplitReasonsManage, "مدیریت دلایل تقسیم", "مدیریت فهرست دلایل تقسیم مأموریت."),
            new(Reports.View, "مشاهده گزارش‌ها", "مشاهده گزارش‌های ناوگان."),
            new(Reports.Export, "خروجی گزارش‌ها", "دریافت خروجی Excel و PDF گزارش‌ها."),
            new(Notifications.View, "مشاهده اعلان‌ها", "مشاهده اعلان‌های داخلی کاربر."),
            new(Notifications.SmsView, "مشاهده گزارش پیامک", "مشاهده صف و سوابق پیامک با مجوز مستقل."),
            new(Notifications.SmsRetry, "ارسال مجدد پیامک", "ارسال مجدد دستی پیامک ناموفق."),
            new(Notifications.SmsTemplatesManage, "مدیریت قالب پیامک", "مدیریت قالب‌های رویداد پیامکی."),
            new(Security.AuditView, "مشاهده گزارش ممیزی", "مشاهده سوابق ممیزی با مجوز مستقل."),
            new(Security.SessionsView, "مشاهده سوابق نشست", "مشاهده سوابق ورود و نشست کاربران."),
            new(Administration.AuthorizationManage, "مدیریت نقش‌ها و مجوزها", "مدیریت نقش‌ها، نگاشت AD و مجوزهای مستقیم."),
            new(Administration.ConnectionsManage, "مدیریت اتصال سرویس‌ها", "تغییر و تست اتصال دیتابیس، اکتیو دایرکتوری و سامانه پیامک."),
            new(Administration.SessionsManage, "مدیریت تنظیمات نشست", "تغییر عمر نشست و زمان عدم فعالیت."),
            new(Administration.SystemManage, "مدیریت تنظیمات سامانه", "مدیریت تنظیمات سازمانی، AD و قواعد سامانه."),
            new(Administration.UsersManage, "مدیریت کاربران", "فعال‌سازی، غیرفعال‌سازی و مدیریت پروفایل کاربران."),
            new(Administration.LocationsManage, "مدیریت مکان‌ها", "تأیید، رد، ویرایش و غیرفعال‌سازی مکان‌ها.")
        ]);

    private static readonly HashSet<string> KnownCodes =
        new(DefinitionsValue.Select(item => item.Code), StringComparer.OrdinalIgnoreCase);

    /// <summary>پیشوندهای wildcard مجاز؛ wildcard فقط برای ماژول‌های عملیاتی تعریف شده است.</summary>
    private static readonly string[] WildcardPrefixes = ["drivers.", "vehicles.", "missions."];

    public static IReadOnlyList<PermissionDefinition> Definitions => DefinitionsValue;

    public static bool Contains(string permission) =>
        !string.IsNullOrWhiteSpace(permission) && KnownCodes.Contains(permission.Trim());

    /// <summary>آیا الگوی مجوز برای ذخیره قابل قبول است؟ wildcard فقط برای پیشوندهای مجاز.</summary>
    public static bool IsAssignable(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var code = permission.Trim();
        if (Contains(code))
        {
            return true;
        }

        return code.EndsWith(".*", StringComparison.Ordinal)
            && WildcardPrefixes.Contains(code[..^1], StringComparer.OrdinalIgnoreCase);
    }

    public static string Policy(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return $"Permission:{permission}";
    }
}

/// <summary>مشخصات نمایشی و فنی یک مجوز ثبت‌شده در کاتالوگ مرکزی.</summary>
public sealed record PermissionDefinition(string Code, string Title, string Description);
