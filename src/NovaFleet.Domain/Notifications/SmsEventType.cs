namespace NovaFleet.Domain.Notifications;

/// <summary>
/// رخدادهای دارای پیامک. کلید متنی هر عضو برای ذخیره در پایگاه داده و قالب‌های پیام استفاده می‌شود
/// تا افزودن رخداد جدید بدون تغییر مقادیر ذخیره‌شده قبلی ممکن باشد.
/// </summary>
public enum SmsEventType
{
    /// <summary>تأیید مأموریت توسط بررسی‌کننده.</summary>
    MissionApproved = 1,

    /// <summary>رد مأموریت همراه دلیل.</summary>
    MissionRejected = 2,

    /// <summary>تغییر زمان یا مسیر مأموریت تأییدشده.</summary>
    MissionChanged = 3,

    /// <summary>تغییر راننده یا خودروی مأموریت.</summary>
    DriverOrVehicleChanged = 4,

    /// <summary>لغو مأموریت.</summary>
    MissionCancelled = 5,

    /// <summary>تأیید انصراف از مأموریت.</summary>
    WithdrawalApproved = 6,

    /// <summary>رد انصراف از مأموریت.</summary>
    WithdrawalRejected = 7,

    /// <summary>یادآوری نزدیک شدن زمان شروع مأموریت.</summary>
    MissionStartReminder = 8,

    /// <summary>پایان مأموریت.</summary>
    MissionCompleted = 9,

    /// <summary>پیام آزمون دستی اپراتور؛ در فهرست رویدادهای مأموریت شمرده نمی‌شود.</summary>
    ManualTest = 10,
}

/// <summary>نام‌های پایدار رخداد برای ذخیره‌سازی و تطبیق با قالب پیامک.</summary>
public static class SmsEventNames
{
    private static readonly Dictionary<SmsEventType, string> Names = new()
    {
        [SmsEventType.MissionApproved] = "mission.approved",
        [SmsEventType.MissionRejected] = "mission.rejected",
        [SmsEventType.MissionChanged] = "mission.changed",
        [SmsEventType.DriverOrVehicleChanged] = "mission.assignment.changed",
        [SmsEventType.MissionCancelled] = "mission.cancelled",
        [SmsEventType.WithdrawalApproved] = "mission.withdrawal.approved",
        [SmsEventType.WithdrawalRejected] = "mission.withdrawal.rejected",
        [SmsEventType.MissionStartReminder] = "mission.start.reminder",
        [SmsEventType.MissionCompleted] = "mission.completed",
        [SmsEventType.ManualTest] = "sms.test",
    };

    public static string Of(SmsEventType type) =>
        Names.TryGetValue(type, out var name) ? name : throw new ArgumentOutOfRangeException(nameof(type));

    public static bool TryParse(string? value, out SmsEventType type)
    {
        foreach (var pair in Names)
        {
            if (string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                type = pair.Key;
                return true;
            }
        }

        type = default;
        return false;
    }
}
