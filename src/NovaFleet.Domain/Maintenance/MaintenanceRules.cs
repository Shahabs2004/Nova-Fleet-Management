using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Maintenance;

public static class MaintenanceRules
{
    public static readonly string[] AssetKeys = ["oil-can", "oil-filter", "air-filter", "old-tire", "old-brake-pad", "old-battery", "timing-belt", "engine", "transmission", "radiator", "electrical", "inspection", "general-service"];

    public static void ValidateIntervals(long? km, int? months, int warningKm, int warningDays)
    {
        if (km is null && months is null)
            throw new ArgumentException("حداقل یکی از دوره‌های کیلومتر یا ماه شمسی لازم است.");
        if (km is <= 0 or > 10000000 || months is <= 0 or > 120)
            throw new ArgumentException("دورهٔ کیلومتر یا ماه شمسی معتبر نیست.");
        if (warningKm is < 0 or > 1000000 || warningDays is < 0 or > 3650)
            throw new ArgumentException("آستانهٔ پیش‌هشدار معتبر نیست.");
    }

    public static void ValidateKm(long? km)
    {
        if (km is < 0 or > 999999999)
            throw new ArgumentException("کیلومتر باید عددی بین صفر و ۹۹۹۹۹۹۹۹۹ باشد.");
    }

    public static decimal Money(decimal value)
    {
        if (value < 0 || value > 999999999999m || decimal.Truncate(value) != value)
            throw new ArgumentException("مبلغ باید عدد صحیح و نامنفی به تومان باشد.");
        return value;
    }

    public static string Required(string? value, int max, string label)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result) || result.Length > max)
            throw new ArgumentException($"{label} الزامی است و حداکثر {max} نویسه دارد.");
        return result;
    }

    public static string? Optional(string? value, int max)
    {
        var result = value?.Trim();
        if (result?.Length > max)
            throw new ArgumentException($"متن نباید بیش از {max} نویسه داشته باشد.");
        return string.IsNullOrEmpty(result) ? null : result;
    }

    public static string DueState(long? dueKm, DateTimeOffset? dueAt, long? currentKm,
        DateTimeOffset now, int warningKm, int warningDays, bool invalidOdometer)
    {
        var today = JalaliDate.ToTehranDateTime(now).Date;
        var dueDate = dueAt.HasValue ? JalaliDate.ToTehranDateTime(dueAt.Value).Date : (DateTime?)null;
        if ((dueDate.HasValue && dueDate <= today) || (!invalidOdometer && currentKm.HasValue && dueKm.HasValue && currentKm >= dueKm))
            return "due";
        if ((dueDate.HasValue && dueDate <= today.AddDays(warningDays)) || (!invalidOdometer && currentKm.HasValue && dueKm.HasValue && dueKm-currentKm <= warningKm))
            return "approaching";
        if ((dueKm.HasValue && (invalidOdometer || !currentKm.HasValue)) || (!dueKm.HasValue && !dueAt.HasValue))
            return "unknown";
        return "healthy";
    }
}
