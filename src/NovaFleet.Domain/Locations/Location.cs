using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Locations;

/// <summary>
/// مکان قابل استفاده به‌عنوان مبدأ، مقصد یا توقف میانی.
/// کاربران می‌توانند پیشنهاد بدهند و فقط مدیر مجاز آن را تأیید یا رد می‌کند.
/// </summary>
public sealed class Location : Entity
{
    private Location()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public LocationStatus Status { get; private set; } = LocationStatus.Pending;

    public string ProposedBy { get; private set; } = string.Empty;

    public string? ReviewedBy { get; private set; }

    public string? ReviewReason { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public Location(string name, string proposedBy, bool autoApprove = false)
    {
        Rename(name);
        ProposedBy = Required(proposedBy, 64, nameof(proposedBy));
        if (autoApprove)
        {
            Status = LocationStatus.Approved;
            ReviewedBy = proposedBy;
            ReviewedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        var normalized = name.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "نام مکان حداکثر ۲۰۰ نویسه است.");
        }

        Name = normalized;
    }

    public void Approve(string reviewer)
    {
        EnsurePending();
        ReviewedBy = Required(reviewer, 64, nameof(reviewer));
        ReviewReason = null;
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = LocationStatus.Approved;
    }

    public void Reject(string reviewer, string reason)
    {
        EnsurePending();
        ReviewedBy = Required(reviewer, 64, nameof(reviewer));
        ReviewReason = Required(reason, 1000, nameof(reason));
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = LocationStatus.Rejected;
    }

    public void Disable(string reviewer, string reason)
    {
        if (Status == LocationStatus.Disabled)
        {
            return;
        }

        ReviewedBy = Required(reviewer, 64, nameof(reviewer));
        ReviewReason = Required(reason, 1000, nameof(reason));
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = LocationStatus.Disabled;
    }

    public void Reopen(string reviewer)
    {
        ReviewedBy = Required(reviewer, 64, nameof(reviewer));
        ReviewReason = null;
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = LocationStatus.Approved;
    }

    private void EnsurePending()
    {
        if (Status != LocationStatus.Pending)
        {
            throw new InvalidOperationException("فقط مکان در انتظار تأیید قابل بررسی است.");
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
