namespace NovaFleet.Domain.Missions;

/// <summary>وضعیت چرخه عمر درخواست یا مأموریت.</summary>
public enum MissionStatus
{
    Pending = 1,
    UnderReview = 2,
    Confirmed = 3,
    Running = 4,
    Completed = 5,
    WithdrawalPending = 6,
    Cancelled = 7,
    Rejected = 8
}
