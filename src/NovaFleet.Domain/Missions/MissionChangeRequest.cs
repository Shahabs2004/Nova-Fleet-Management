using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Missions;

/// <summary>
/// درخواست تغییر مأموریت پس از تأیید؛ تا تصمیم نهایی اطلاعات قبلی معتبر است.
/// </summary>
public sealed class MissionChangeRequest : Entity
{
    private MissionChangeRequest()
    {
    }

    public Guid MissionId { get; private set; }

    public string RequesterUsername { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public string ProposedValuesJson { get; private set; } = "{}";

    public MissionChangeRequestStatus Status { get; private set; } = MissionChangeRequestStatus.Pending;

    public string? ReviewerUsername { get; private set; }

    public DateTimeOffset? ReviewStartedAtUtc { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public string? DecisionReason { get; private set; }

    public MissionChangeRequest(Guid missionId, string requesterUsername, string reason, string proposedValuesJson)
    {
        if (missionId == Guid.Empty)
        {
            throw new ArgumentException("شناسه مأموریت معتبر نیست.", nameof(missionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(requesterUsername, nameof(requesterUsername));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedValuesJson, nameof(proposedValuesJson));

        MissionId = missionId;
        RequesterUsername = requesterUsername.Trim();
        Reason = reason.Trim();
        ProposedValuesJson = proposedValuesJson;
    }

    public void StartReview(string reviewer, DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionChangeRequestStatus.Pending);
        ReviewerUsername = Required(reviewer, 64, nameof(reviewer));
        ReviewStartedAtUtc = nowUtc;
        Status = MissionChangeRequestStatus.UnderReview;
    }

    public void Approve(string reviewer, string? decisionReason, DateTimeOffset nowUtc)
    {
        EnsureReviewer(reviewer);
        EnsureStatus(MissionChangeRequestStatus.UnderReview);
        DecisionReason = string.IsNullOrWhiteSpace(decisionReason) ? "درخواست تغییر تأیید و اعمال شد." : decisionReason.Trim();
        ReviewedAtUtc = nowUtc;
        Status = MissionChangeRequestStatus.Approved;
    }

    public void Reject(string reviewer, string reason, DateTimeOffset nowUtc)
    {
        EnsureReviewer(reviewer);
        EnsureStatus(MissionChangeRequestStatus.UnderReview);
        DecisionReason = Required(reason, 1000, nameof(reason));
        ReviewedAtUtc = nowUtc;
        Status = MissionChangeRequestStatus.Rejected;
    }

    private void EnsureReviewer(string reviewer)
    {
        if (!string.Equals(ReviewerUsername, reviewer, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("فقط بازبینی که بررسی را آغاز کرده می‌تواند تصمیم نهایی را ثبت کند.");
        }
    }

    private void EnsureStatus(MissionChangeRequestStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException("عملیات با وضعیت فعلی درخواست تغییر سازگار نیست.");
        }
    }

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
