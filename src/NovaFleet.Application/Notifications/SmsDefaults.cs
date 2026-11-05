using NovaFleet.Domain.Notifications;

namespace NovaFleet.Application.Notifications;

/// <summary>قالب و گیرندگان پیش‌فرض هر رخداد؛ مدیر سامانه می‌تواند آن‌ها را تغییر دهد.</summary>
public static class SmsDefaults
{
    public static string TitleFor(SmsEventType type) => type switch
    {
        SmsEventType.MissionApproved => "تأیید مأموریت",
        SmsEventType.MissionRejected => "رد درخواست",
        SmsEventType.MissionChanged => "تغییر زمان/مشخصات",
        SmsEventType.DriverOrVehicleChanged => "تغییر راننده یا خودرو",
        SmsEventType.MissionCancelled => "لغو مأموریت",
        SmsEventType.WithdrawalApproved => "تأیید انصراف",
        SmsEventType.WithdrawalRejected => "رد انصراف",
        SmsEventType.MissionStartReminder => "یادآوری شروع",
        SmsEventType.MissionCompleted => "پایان مأموریت",
        _ => "پیامک سامانه"
    };

    public static string BodyFor(SmsEventType type) => type switch
    {
        SmsEventType.MissionApproved =>
            "مأموریت {missionNumber} تأیید شد.\nراننده: {driver}\nخودرو: {plate}\nشروع: {start}\nمبدأ: {origin}\nمقصد: {destination}",
        SmsEventType.MissionRejected =>
            "درخواست مأموریت {missionNumber} رد شد.\nعلت: {reason}",
        SmsEventType.MissionChanged =>
            "زمان یا مشخصات مأموریت {missionNumber} تغییر کرد.\nشروع جدید: {start}\nپایان جدید: {end}",
        SmsEventType.DriverOrVehicleChanged =>
            "راننده یا خودروی مأموریت {missionNumber} تغییر کرد.\nراننده: {driver}\nخودرو: {plate}",
        SmsEventType.MissionCancelled =>
            "مأموریت {missionNumber} لغو شد.\nعلت: {reason}",
        SmsEventType.WithdrawalApproved =>
            "انصراف از مأموریت {missionNumber} تأیید شد.",
        SmsEventType.WithdrawalRejected =>
            "انصراف از مأموریت {missionNumber} رد شد.\nعلت: {reason}",
        SmsEventType.MissionStartReminder =>
            "یادآوری مأموریت {missionNumber}\nشروع: {start}\nمبدأ: {origin}\nمقصد: {destination}\nراننده: {driver}\nخودرو: {plate}",
        SmsEventType.MissionCompleted =>
            "مأموریت {missionNumber} خاتمه یافت.\nمسافت: {distance} کیلومتر",
        _ => "مأموریت {missionNumber}: {title}"
    };

    /// <summary>گیرندگان پیش‌فرض؛ مقادیر ذخیره‌شده در تنظیمات بر این پیش‌فرض مقدم‌اند.</summary>
    public static IReadOnlyList<string> RecipientsFor(SmsEventType type) => type switch
    {
        SmsEventType.MissionRejected => ["requester"],
        SmsEventType.DriverOrVehicleChanged => ["driver"],
        SmsEventType.MissionStartReminder => ["driver"],
        _ => ["requester", "driver"]
    };

    public static IReadOnlyList<string> ParseRecipients(string? value, SmsEventType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RecipientsFor(type);
        }

        var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item is "requester" or "driver")
            .Distinct()
            .ToList();
        return parsed.Count > 0 ? parsed : RecipientsFor(type);
    }
}
