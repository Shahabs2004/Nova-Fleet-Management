namespace NovaFleet.Domain.Missions;

/// <summary>وضعیت درخواست تغییر پس از تأیید مأموریت.</summary>
public enum MissionChangeRequestStatus
{
    Pending = 1,
    UnderReview = 2,
    Approved = 3,
    Rejected = 4
}
