namespace NovaFleet.Domain.Vehicles;

/// <summary>وضعیت دستی خودرو که توسط کاربران مجاز تغییر می‌کند.</summary>
public enum ManualVehicleStatus
{
    Ready = 1,
    RepairShop = 2,
    OutOfService = 3
}
