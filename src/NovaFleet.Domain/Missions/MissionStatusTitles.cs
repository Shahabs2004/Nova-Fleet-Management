namespace NovaFleet.Domain.Missions;

/// <summary>نمایش فارسی وضعیت مأموریت در گزارش‌ها و سند حکم.</summary>
public static class MissionStatusTitles
{
    public static string Of(MissionStatus status) => status switch
    {
        MissionStatus.Pending => "در انتظار بررسی",
        MissionStatus.UnderReview => "در حال بررسی",
        MissionStatus.Confirmed => "تأییدشده",
        MissionStatus.Running => "در حال اجرا",
        MissionStatus.Completed => "تکمیل‌شده",
        MissionStatus.WithdrawalPending => "در انتظار تصمیم انصراف",
        MissionStatus.Cancelled => "لغوشده",
        MissionStatus.Rejected => "ردشده",
        _ => status.ToString()
    };
}
