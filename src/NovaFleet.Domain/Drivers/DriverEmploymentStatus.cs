namespace NovaFleet.Domain.Drivers;

/// <summary>وضعیت همکاری راننده؛ فقط راننده فعال قابل تخصیص است.</summary>
public enum DriverEmploymentStatus
{
    Active = 1,
    OnLeave = 2,
    Terminated = 3,
    Inactive = 4
}
