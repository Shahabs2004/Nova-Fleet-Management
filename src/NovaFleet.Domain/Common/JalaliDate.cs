using System.Globalization;

namespace NovaFleet.Domain.Common;

/// <summary>
/// تبدیل و قالب‌بندی تاریخ شمسی برای قواعد دامنه مانند شماره‌گذاری سالانه مأموریت.
/// ذخیره‌سازی همیشه UTC است و تبدیل فقط در مرز نمایش یا قاعده تجاری انجام می‌شود.
/// </summary>
public static class JalaliDate
{
    /// <summary>تهران از سال ۱۴۰۱ ساعت تابستانی ندارد؛ اختلاف ثابت سه‌ونیم ساعت است.</summary>
    public static readonly TimeSpan TehranOffset = TimeSpan.FromMinutes(210);

    private static readonly PersianCalendar Calendar = new();

    public static DateTimeOffset ToTehran(DateTimeOffset value) => value.ToOffset(TehranOffset);

    public static DateTime ToTehranDateTime(DateTimeOffset value) => ToTehran(value).DateTime;

    public static int YearOf(DateTimeOffset value) => Calendar.GetYear(ToTehranDateTime(value));

    public static int MonthOf(DateTimeOffset value) => Calendar.GetMonth(ToTehranDateTime(value));

    public static int DayOf(DateTimeOffset value) => Calendar.GetDayOfMonth(ToTehranDateTime(value));

    public static string FormatDate(DateTimeOffset value)
    {
        var local = ToTehranDateTime(value);
        return $"{Calendar.GetYear(local):D4}/{Calendar.GetMonth(local):D2}/{Calendar.GetDayOfMonth(local):D2}";
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        var local = ToTehranDateTime(value);
        return $"{FormatDate(value)} {local.Hour:D2}:{local.Minute:D2}";
    }

    /// <summary>آغاز روز شمسی به وقت تهران و بازگشت آن به UTC.</summary>
    public static DateTimeOffset StartOfTehranDayUtc(int year, int month, int day)
    {
        var local = Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);
        return new DateTimeOffset(local, TehranOffset).ToUniversalTime();
    }
}
