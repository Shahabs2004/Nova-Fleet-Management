namespace NovaFleet.Domain.Locations;

/// <summary>وضعیت مکان در چرخه پیشنهاد و تأیید.</summary>
public enum LocationStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Disabled = 4
}
