namespace NovaFleet.Domain.Vehicles;

/// <summary>وضعیت مؤثر خودرو؛ وضعیت مأموریت بر وضعیت دستی اولویت دارد.</summary>
public enum VehicleEffectiveStatus
{
    Ready = 1,
    InMission = 2,
    RepairShop = 3,
    OutOfService = 4
}
