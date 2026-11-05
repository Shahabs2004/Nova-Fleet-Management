using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Vehicles;

/// <summary>
/// خودروی ناوگان؛ پلاک به‌صورت ساختاریافته ذخیره می‌شود تا جست‌وجو و نمایش گرافیکی دقیق باشد.
/// وضعیت مؤثر از ترکیب وضعیت دستی و مأموریت فعال محاسبه می‌شود.
/// </summary>
public sealed class Vehicle : Entity
{
    private Vehicle()
    {
    }

    public string Brand { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    public string PlateFirstTwo { get; private set; } = string.Empty;

    public string PlateLetter { get; private set; } = string.Empty;

    public string PlateNextThree { get; private set; } = string.Empty;

    public string PlateIranCode { get; private set; } = string.Empty;

    public ManualVehicleStatus ManualStatus { get; private set; } = ManualVehicleStatus.Ready;

    public bool IsArchived { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IranianLicensePlate LicensePlate
        => IranianLicensePlate.Create(PlateFirstTwo, PlateLetter, PlateNextThree, PlateIranCode);

    public string PlateDisplay => LicensePlate.Format();

    public string DisplayName => $"{Brand} {Model} — {PlateDisplay}";

    public Vehicle(string brand, string model, IranianLicensePlate licensePlate)
    {
        UpdateDetails(brand, model, licensePlate);
        ManualStatus = ManualVehicleStatus.Ready;
    }

    public void UpdateDetails(string brand, string model, IranianLicensePlate licensePlate)
    {
        ArgumentNullException.ThrowIfNull(licensePlate);
        Brand = Required(brand, 100, nameof(brand));
        Model = Required(model, 100, nameof(model));
        PlateFirstTwo = licensePlate.FirstTwo;
        PlateLetter = licensePlate.Letter;
        PlateNextThree = licensePlate.NextThree;
        PlateIranCode = licensePlate.IranCode;
    }

    public void ChangeManualStatus(ManualVehicleStatus status, bool hasActiveMission)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "وضعیت خودرو معتبر نیست.");
        }

        if (hasActiveMission && status != ManualVehicleStatus.Ready)
        {
            throw new InvalidOperationException("تا پایان مأموریت فعال، تغییر وضعیت خودرو به تعمیرگاه یا خارج از سرویس مجاز نیست.");
        }

        ManualStatus = status;
    }

    public VehicleEffectiveStatus GetEffectiveStatus(bool hasActiveMission)
    {
        if (hasActiveMission)
        {
            return VehicleEffectiveStatus.InMission;
        }

        return ManualStatus switch
        {
            ManualVehicleStatus.Ready => VehicleEffectiveStatus.Ready,
            ManualVehicleStatus.RepairShop => VehicleEffectiveStatus.RepairShop,
            ManualVehicleStatus.OutOfService => VehicleEffectiveStatus.OutOfService,
            _ => throw new InvalidOperationException("وضعیت دستی خودرو ناشناخته است.")
        };
    }

    public void Archive() => IsArchived = true;

    public void Reactivate()
    {
        if (ManualStatus != ManualVehicleStatus.Ready)
        {
            throw new InvalidOperationException("خودروی تعمیرگاهی یا خارج از سرویس قابل بازگردانی از بایگانی نیست.");
        }

        IsArchived = false;
    }

    /// <summary>آمادگی ذاتی خودرو؛ تداخل زمانی با سایر مأموریت‌ها بر پایه زمان برنامه‌ریزی‌شده سنجیده می‌شود، نه صرف وجود مأموریت فعال.</summary>
    public bool IsReadyForAssignment => !IsArchived && ManualStatus == ManualVehicleStatus.Ready;

    public bool CanBeAssignedToMission(bool hasActiveMission)
        => IsReadyForAssignment && !hasActiveMission;

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
}
