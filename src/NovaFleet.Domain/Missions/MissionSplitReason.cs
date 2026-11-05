using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Missions;

/// <summary>دلیل قابل مدیریت برای تقسیم مأموریت؛ دلایل غیرفعال در سوابق باقی می‌مانند.</summary>
public sealed class MissionSplitReason : Entity
{
    private MissionSplitReason()
    {
    }

    public string Title { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public MissionSplitReason(string title, int sortOrder = 0)
    {
        Rename(title);
        SortOrder = sortOrder;
    }

    public void Rename(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        var normalized = title.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(title), "عنوان دلیل تقسیم حداکثر ۲۰۰ نویسه است.");
        }

        Title = normalized;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;
}
