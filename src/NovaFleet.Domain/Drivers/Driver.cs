using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Drivers;

/// <summary>
/// اطلاعات پایه راننده و وضعیت همکاری او.
/// خودروی پیش‌فرض فقط یک پیشنهاد است و تخصیص واقعی هر مأموریت مستقل ثبت می‌شود.
/// </summary>
public sealed class Driver : Entity
{
    private Driver()
    {
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string MobilePhone { get; private set; } = string.Empty;

    public DriverEmploymentStatus EmploymentStatus { get; private set; } = DriverEmploymentStatus.Active;

    public Guid? DefaultVehicleId { get; private set; }

    public bool IsArchived { get; private set; }

    /// <summary>نسخه ردیف برای کنترل هم‌زمانی ویرایش.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public Driver(string firstName, string lastName, string mobilePhone)
    {
        UpdateProfile(firstName, lastName, mobilePhone);
        EmploymentStatus = DriverEmploymentStatus.Active;
    }

    public void UpdateProfile(string firstName, string lastName, string mobilePhone)
    {
        FirstName = Required(firstName, 100, nameof(firstName));
        LastName = Required(lastName, 100, nameof(lastName));
        MobilePhone = NormalizeMobile(mobilePhone);
    }

    public void ChangeEmploymentStatus(DriverEmploymentStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "وضعیت همکاری راننده معتبر نیست.");
        }

        EmploymentStatus = status;
    }

    public void SetDefaultVehicle(Guid? vehicleId)
    {
        if (vehicleId == Guid.Empty)
        {
            throw new ArgumentException("شناسه خودروی پیش‌فرض معتبر نیست.", nameof(vehicleId));
        }

        DefaultVehicleId = vehicleId;
    }

    public void Archive()
    {
        IsArchived = true;
    }

    public void Reactivate()
    {
        if (EmploymentStatus != DriverEmploymentStatus.Active)
        {
            throw new InvalidOperationException("فقط راننده با وضعیت فعال قابل بازگردانی از بایگانی است.");
        }

        IsArchived = false;
    }

    public bool CanBeAssignedToMission()
        => !IsArchived && EmploymentStatus == DriverEmploymentStatus.Active;

    private static string Required(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"حداکثر طول مجاز {maxLength} نویسه است.");
        }

        return normalized;
    }

    private static string NormalizeMobile(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var normalized = new string(value.Trim().Where(char.IsDigit).ToArray());
        if (normalized.StartsWith("0098", StringComparison.Ordinal))
        {
            normalized = "0" + normalized[4..];
        }
        else if (normalized.StartsWith("98", StringComparison.Ordinal) && normalized.Length == 12)
        {
            normalized = "0" + normalized[2..];
        }

        if (normalized.Length != 11 || !normalized.StartsWith('0'))
        {
            throw new ArgumentException("شماره موبایل باید ۱۱ رقم و با صفر شروع شود.", nameof(value));
        }

        return normalized;
    }
}
