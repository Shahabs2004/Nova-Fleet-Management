using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Missions;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Missions;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Settings;

namespace NovaFleet.UnitTests.Missions;

/// <summary>
/// سه تفاوت provider میان SQL Server تولیدی و SQLite در آزمون خنثی می‌شود، بدون هیچ تغییری در کد تولیدی:
/// ستون‌های nvarchar(max) که در SQLite ساخته نمی‌شوند، ستون‌های rowversion که خودکار مقدار می‌گیرند،
/// و ستون‌های DateTimeOffset که SQLite در ORDER BY از آن‌ها پشتیبانی نمی‌کند.
/// </summary>
internal sealed class SqliteProviderColumnTypesModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    private static readonly ValueConverter DateTimeToUnixMilliseconds = new ValueConverter<DateTimeOffset, long>(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property[RelationalAnnotationNames.ColumnType] is string columnType
                    && columnType.Contains("(max)", StringComparison.OrdinalIgnoreCase))
                {
                    property[RelationalAnnotationNames.ColumnType] = null;
                }

                if (UnderlyingType(property.ClrType) == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(DateTimeToUnixMilliseconds);
                }

                if (property.ClrType == typeof(byte[])
                    && property.ValueGenerated == ValueGenerated.OnAddOrUpdate
                    && property.IsConcurrencyToken)
                {
                    // SQLite مولّد rowversion ندارد؛ پیش‌فرض ثابت با تولید در درج جایگزین می‌شود
                    // تا قید NOT NULL نقض نشود و به‌روزرسانی‌ها شرط همزمانی نداشته باشند.
                    property[RelationalAnnotationNames.DefaultValue] = new byte[1];
                    property.ValueGenerated = ValueGenerated.OnAdd;
                    property.IsConcurrencyToken = false;
                }
            }
        }
    }

    private static Type UnderlyingType(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}

/// <summary>
/// بستر آزمون مأموریت: پایگاه داده SQLite در حافظه برای هر آزمون، با دسترسی مستقیم به همان
/// NovaFleetDbContext سرویس تولیدی. زمان‌ها همیشه روی ساعت کامل به وقت تهران ساخته می‌شوند
/// چون دامنه آن را الزام می‌کند (Mission.ValidateHour).
/// </summary>
public abstract class MissionTestBase : IDisposable
{
    private static readonly TimeSpan TehranOffset = TimeSpan.FromMinutes(210);

    protected const string RequesterUsername = "demo.requester";
    protected const string OtherUsername = "demo.other";
    protected const string ApproverUsername = "demo.approver";

    protected MissionTestBase()
    {
        Connection = new SqliteConnection("Filename=:memory:");
        Connection.Open();
        Options = new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlite(Connection)
            .ReplaceService<IModelCustomizer, SqliteProviderColumnTypesModelCustomizer>()
            .Options;
        Db = new NovaFleetDbContext(Options);
        Db.Database.EnsureCreated();

        Clock = new FakeClock(Hour(0, 8));
        Audit = new FakeAuditTrail();
        Notifications = new FakeNotificationPublisher();
        Sms = new FakeSmsDispatcher();
        Settings = new FakeSettingsService();
        Runtime = new RuntimeSettings(Db, Clock, new FakeSecretProtector());

        Db.Users.Add(new ApplicationUser(RequesterUsername, "مریم", "رضایی")
            .WithOrganizationalUnit("واحد ترانسفر"));
        Db.Users.Add(new ApplicationUser(OtherUsername, "بهنام", "کاظمی")
            .WithOrganizationalUnit("واحد ترانسفر"));
        Db.Users.Add(new ApplicationUser(ApproverUsername, "قاسم", "سلطانی")
            .WithOrganizationalUnit("واحد پشتیبانی"));
        Db.SaveChanges();
    }

    protected SqliteConnection Connection { get; }

    protected DbContextOptions<NovaFleetDbContext> Options { get; }

    protected NovaFleetDbContext Db { get; }

    protected FakeClock Clock { get; }

    protected FakeAuditTrail Audit { get; }

    protected FakeNotificationPublisher Notifications { get; }

    protected FakeSmsDispatcher Sms { get; }

    protected FakeSettingsService Settings { get; }

    /// <summary>تنظیمات runtime واقعی روی همان SQLite؛ محافظ رمز در آزمون قابل‌برگشت است.</summary>
    protected RuntimeSettings Runtime { get; }

    /// <summary>ساعت تهرانِ کامل؛ day شنبه ۱۵ آبان ۱۴۰۵ را به عنوان «امروز» مبنا قرار می‌دهد.</summary>
    protected static DateTimeOffset Hour(int day, int hour)
        => new DateTimeOffset(2026, 11, 15, 0, 0, 0, TehranOffset).AddDays(day).AddHours(hour);

    protected MissionService ServiceFor(ICurrentUser user) =>
        new(Db, user, Audit, Clock, Settings, Notifications, Sms);

    protected static FakeCurrentUser User(string username, params string[] permissions) =>
        new(username, username, EffectivePermissionSet.Build(
            permissions.Select(code => (code, PermissionEffect.Grant))));

    /// <summary>کاربر با همه مجوزهای مأموریت که مسیر شاد را می‌سازد.</summary>
    protected static FakeCurrentUser MissionAdmin(string username = RequesterUsername) =>
        new(username, username, EffectivePermissionSet.Build(AllMissionPermissions()
            .Select(code => (code, PermissionEffect.Grant))));

    protected static IEnumerable<string> AllMissionPermissions() =>
    [
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
        PermissionCatalog.Missions.OdometerManage,
        PermissionCatalog.Missions.WithdrawalReview,
        PermissionCatalog.Missions.ChangeReview,
    ];

    protected static MissionRequestInput Request(
        DateTimeOffset start,
        DateTimeOffset end,
        string? organizationalUnit = null,
        string title = "ارسال بار به بندرعباس") =>
        new(
            title,
            MissionType.Freight,
            "انتقال کالای گروه تولید",
            "شیراز",
            ["جهرم"],
            "بندرعباس",
            start,
            end,
            "گروه ترانسفر",
            "09123456789",
            organizationalUnit,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    protected static MissionRequestInput ValidRequest() => Request(Hour(0, 10), Hour(0, 14));

    protected static MissionChangeInput ProposedChange(
        MissionDetailsDto mission,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? driverId = null,
        Guid? vehicleId = null,
        long? odometerStartKm = null) =>
        new(
            mission.Title,
            mission.Type,
            mission.Purpose,
            mission.Origin,
            mission.IntermediateStops,
            mission.Destination,
            start,
            end,
            mission.RequestingGroup,
            mission.RequesterPhoneSnapshot,
            mission.Description,
            mission.PassengerCount,
            mission.MissionOrderNumber,
            mission.MissionOrderDate,
            mission.CargoDescription,
            mission.ConsigneeName,
            mission.PassengerNames,
            driverId ?? mission.DriverId,
            vehicleId ?? mission.VehicleId,
            odometerStartKm);

    protected Driver NewDriver(string firstName = "رضا", string lastName = "محمدی")
    {
        var driver = new Driver(firstName, lastName, "09121112233");
        Db.Drivers.Add(driver);
        return driver;
    }

    /// <summary>پلاک معتبر و یکتا به ازای هر فراخوانی؛ شاخص چهارگانه پلاک روی دیتابیس unique است.</summary>
    private static readonly string[] PlateLetters =
    [
        "ب", "ج", "د", "س", "ص", "ط", "ع", "ق", "ل", "م", "ن", "و", "ه", "ی"
    ];

    private int nextPlateIndex;

    protected Vehicle NewVehicle()
    {
        var index = nextPlateIndex++;
        var plate = IranianLicensePlate.Create(
            $"{index / PlateLetters.Length + 10:00}",
            PlateLetters[index % PlateLetters.Length],
            $"{index + 100:000}",
            "40");
        var vehicle = new Vehicle("ایویکو", "دیلی", plate);
        Db.Vehicles.Add(vehicle);
        return vehicle;
    }

    /// <summary>راننده و خودروی آماده به کار را ثبت و ذخیره می‌کند.</summary>
    protected (Driver Driver, Vehicle Vehicle) SaveReadyResources()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        Db.SaveChanges();
        return (driver, vehicle);
    }

    protected Task<MissionDetailsDto> CreatePendingAsync(ICurrentUser? user = null) =>
        ServiceFor(user ?? MissionAdmin()).CreateAsync(ValidRequest());

    /// <summary>ثبت، بررسی و تأیید؛ مأموریت در وضعیت Confirmed با منابع تخصیص‌یافته.</summary>
    protected async Task<MissionDetailsDto> ConfirmAsync(
        (Driver Driver, Vehicle Vehicle)? resources = null,
        DateTimeOffset? plannedStart = null,
        DateTimeOffset? plannedEnd = null)
    {
        var admin = MissionAdmin();
        var start = plannedStart ?? Hour(0, 10);
        var end = plannedEnd ?? Hour(0, 14);
        var created = await ServiceFor(admin).CreateAsync(Request(start, end));
        await ServiceFor(admin).StartReviewAsync(created.Id);
        var pair = resources ?? SaveReadyResources();
        return await ServiceFor(admin).ApproveAsync(created.Id,
            new MissionApprovalRequest(pair.Driver.Id, pair.Vehicle.Id, "تأیید آزمون"));
    }

    /// <summary>
    /// ورود به وضعیت Running از طریق دامنه انجام می‌شود چون شروع خودکار در اختیار
    /// MissionLifecycleWorker است، نه MissionService.
    /// </summary>
    protected async Task<Mission> MarkRunningAsync(Guid missionId, DateTimeOffset nowUtc, long? odometerStartKm = 1_000)
    {
        Clock.NowUtc = nowUtc;
        var mission = await Db.Missions.FirstAsync(item => item.Id == missionId);
        if (odometerStartKm.HasValue)
        {
            mission.SetOdometerStart(odometerStartKm.Value);
        }

        mission.StartAutomatically(nowUtc);
        await Db.SaveChangesAsync();
        return mission;
    }

    protected Mission Reload(Guid missionId) => Db.Missions.AsNoTracking().First(item => item.Id == missionId);

    protected MissionChangeRequest ReloadChange(Guid changeId) =>
        Db.MissionChangeRequests.AsNoTracking().First(item => item.Id == changeId);

    public void Dispose()
    {
        Db.Dispose();
        Connection.Dispose();
    }
}

internal static class ApplicationUserSeedExtensions
{
    public static ApplicationUser WithOrganizationalUnit(this ApplicationUser user, string organizationalUnit)
    {
        user.UpdateProfile(user.FirstName, user.LastName, user.MobilePhone, user.Email, organizationalUnit);
        return user;
    }
}
