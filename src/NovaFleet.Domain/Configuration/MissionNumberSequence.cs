using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Configuration;

/// <summary>
/// شمارنده شماره مأموریت برای هر سال شمسی. یکتا بودن شماره در سطح سال با کلید اصلی
/// و قفل ردیف در تراکنش شماره‌گذاری تضمین می‌شود.
/// </summary>
public sealed class MissionNumberSequence : Entity
{
    private MissionNumberSequence()
    {
    }

    public int Year { get; private set; }

    public int LastNumber { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public MissionNumberSequence(int year)
    {
        if (year is < 1300 or > 1600)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "سال شماره‌گذاری معتبر نیست.");
        }

        Year = year;
        LastNumber = 0;
    }

    public int Next()
    {
        LastNumber++;
        return LastNumber;
    }

    /// <summary>قالب شماره مأموریت: پیشوند سال شمسی و شماره چهاررقمی مانند <c>1404-0042</c>.</summary>
    public static string Format(int year, int number) => $"{year:0000}-{number:0000}";
}
