using System.Text.Json;
using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Missions;

/// <summary>
/// درخواست و مأموریت در یک چرخه عمر واحد.
/// قواعد تداخل، فاصله و پیوستگی کیلومتر در سرویس کاربردی و تراکنش پایگاه داده کنترل می‌شوند.
/// </summary>
public sealed class Mission : Entity
{
    public const int TehranOffsetMinutes = 210;

    private Mission()
    {
    }

    public string MissionNumber { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string RequesterUsername { get; private set; } = string.Empty;

    public string RequesterName { get; private set; } = string.Empty;

    public string RequesterPhoneSnapshot { get; private set; } = string.Empty;

    public string RequesterOrganizationalUnit { get; private set; } = string.Empty;

    public string RequestingGroup { get; private set; } = string.Empty;

    public MissionType Type { get; private set; }

    public string Purpose { get; private set; } = string.Empty;

    public string Origin { get; private set; } = string.Empty;

    public string Destination { get; private set; } = string.Empty;

    /// <summary>فهرست مرتب مقاصد میانی به‌صورت JSON.</summary>
    public string RouteJson { get; private set; } = "[]";

    public DateTimeOffset PlannedStartAtUtc { get; private set; }

    public DateTimeOffset PlannedEndAtUtc { get; private set; }

    public Guid? DriverId { get; private set; }

    public Guid? VehicleId { get; private set; }

    public string? Description { get; private set; }

    public int? PassengerCount { get; private set; }

    public string? MissionOrderNumber { get; private set; }

    public DateOnly? MissionOrderDate { get; private set; }

    public string? CargoDescription { get; private set; }

    public string? ConsigneeName { get; private set; }

    public string? PassengerNames { get; private set; }

    public MissionStatus Status { get; private set; } = MissionStatus.Pending;

    public DateTimeOffset? ReviewStartedAtUtc { get; private set; }

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public DateTimeOffset? ActualStartAtUtc { get; private set; }

    public DateTimeOffset? ActualEndAtUtc { get; private set; }

    public long? OdometerStartKm { get; private set; }

    public long? OdometerEndKm { get; private set; }

    public string? DecisionReason { get; private set; }

    public MissionStatus? StatusBeforeWithdrawal { get; private set; }

    public Guid? RelatedMissionId { get; private set; }

    public string? SplitReason { get; private set; }

    public DateTimeOffset? SplitAtUtc { get; private set; }

    public string? SplitBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status is MissionStatus.Confirmed or MissionStatus.Running or MissionStatus.WithdrawalPending;

    public bool IsFinal => Status is MissionStatus.Completed or MissionStatus.Cancelled or MissionStatus.Rejected;

    public long? DistanceKm => OdometerStartKm.HasValue && OdometerEndKm.HasValue
        ? OdometerEndKm.Value - OdometerStartKm.Value
        : null;

    public Mission(
        string missionNumber,
        string title,
        string requesterUsername,
        string requesterName,
        string requesterPhoneSnapshot,
        string requesterOrganizationalUnit,
        string requestingGroup,
        MissionType type,
        string purpose,
        string origin,
        string destination,
        IEnumerable<string>? intermediateStops,
        DateTimeOffset plannedStartAtUtc,
        DateTimeOffset plannedEndAtUtc,
        string? description = null,
        int? passengerCount = null,
        string? missionOrderNumber = null,
        DateOnly? missionOrderDate = null,
        string? cargoDescription = null,
        string? consigneeName = null,
        string? passengerNames = null)
    {
        MissionNumber = Required(missionNumber, 20, nameof(missionNumber));
        Title = Required(title, 200, nameof(title));
        RequesterUsername = Required(requesterUsername, 64, nameof(requesterUsername));
        RequesterName = Required(requesterName, 200, nameof(requesterName));
        RequesterPhoneSnapshot = Required(requesterPhoneSnapshot, 20, nameof(requesterPhoneSnapshot));
        RequesterOrganizationalUnit = Required(requesterOrganizationalUnit, 200, nameof(requesterOrganizationalUnit));
        RequestingGroup = Required(requestingGroup, 200, nameof(requestingGroup));
        Purpose = Required(purpose, 500, nameof(purpose));
        Origin = Required(origin, 200, nameof(origin));
        Destination = Required(destination, 200, nameof(destination));

        ValidateType(type);
        ValidateHour(plannedStartAtUtc, nameof(plannedStartAtUtc));
        ValidateHour(plannedEndAtUtc, nameof(plannedEndAtUtc));
        if (plannedEndAtUtc <= plannedStartAtUtc)
        {
            throw new ArgumentException("زمان پایان باید بعد از زمان شروع باشد.", nameof(plannedEndAtUtc));
        }

        if (passengerCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(passengerCount), "تعداد همراهان نمی‌تواند منفی باشد.");
        }

        Type = type;
        PlannedStartAtUtc = plannedStartAtUtc;
        PlannedEndAtUtc = plannedEndAtUtc;
        RouteJson = JsonSerializer.Serialize(NormalizeStops(intermediateStops));
        Description = Optional(description, 2000);
        PassengerCount = passengerCount;
        MissionOrderNumber = Optional(missionOrderNumber, 50);
        MissionOrderDate = missionOrderDate;
        CargoDescription = Optional(cargoDescription, 500);
        ConsigneeName = Optional(consigneeName, 200);
        PassengerNames = Optional(passengerNames, 1000);
        Status = MissionStatus.Pending;
    }

    public IReadOnlyList<string> GetIntermediateStops()
        => JsonSerializer.Deserialize<List<string>>(RouteJson) ?? [];

    public void StartReview(DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.Pending);
        ReviewStartedAtUtc ??= nowUtc;
        Status = MissionStatus.UnderReview;
        UpdatedAtUtc = nowUtc;
    }

    public void Approve(Guid driverId, Guid vehicleId, string approver, DateTimeOffset nowUtc)
    {
        if (Status is not (MissionStatus.Pending or MissionStatus.UnderReview))
        {
            throw new InvalidOperationException("وضعیت فعلی برای تأیید مأموریت مجاز نیست.");
        }

        if (driverId == Guid.Empty || vehicleId == Guid.Empty)
        {
            throw new ArgumentException("راننده و خودرو باید مشخص باشند.");
        }

        DriverId = driverId;
        VehicleId = vehicleId;
        ApprovedBy = Required(approver, 64, nameof(approver));
        ApprovedAtUtc = nowUtc;
        ReviewStartedAtUtc ??= nowUtc;
        Status = MissionStatus.Confirmed;
        UpdatedAtUtc = nowUtc;
    }

    public void Reject(string reason, DateTimeOffset nowUtc)
    {
        if (Status is not (MissionStatus.Pending or MissionStatus.UnderReview))
        {
            throw new InvalidOperationException("فقط درخواست در انتظار بررسی قابل رد است.");
        }

        DecisionReason = Required(reason, 1000, nameof(reason));
        Status = MissionStatus.Rejected;
        UpdatedAtUtc = nowUtc;
    }

    public void StartAutomatically(DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.Confirmed);
        if (nowUtc < PlannedStartAtUtc)
        {
            throw new InvalidOperationException("زمان شروع برنامه‌ریزی‌شده هنوز فرا نرسیده است.");
        }

        if (DriverId is null || VehicleId is null)
        {
            throw new InvalidOperationException("برای شروع مأموریت راننده و خودرو باید تعیین شده باشند.");
        }

        ActualStartAtUtc = PlannedStartAtUtc;
        Status = MissionStatus.Running;
        UpdatedAtUtc = nowUtc;
    }

    public void Extend(DateTimeOffset newPlannedEndAtUtc, string reason, DateTimeOffset nowUtc)
    {
        if (Status is not (MissionStatus.Confirmed or MissionStatus.Running))
        {
            throw new InvalidOperationException("تمدید فقط برای مأموریت تأییدشده یا در حال اجرا مجاز است.");
        }

        ValidateHour(newPlannedEndAtUtc, nameof(newPlannedEndAtUtc));
        if (newPlannedEndAtUtc <= PlannedEndAtUtc)
        {
            throw new ArgumentException("زمان جدید باید بعد از زمان پایان فعلی باشد.", nameof(newPlannedEndAtUtc));
        }

        DecisionReason = Required(reason, 1000, nameof(reason));
        PlannedEndAtUtc = newPlannedEndAtUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void EndEarly(DateTimeOffset endedAtUtc, string reason, DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.Running);
        ValidateHour(endedAtUtc, nameof(endedAtUtc));
        if (endedAtUtc > nowUtc.AddHours(1))
        {
            throw new ArgumentException("زمان خاتمه نمی‌تواند در آینده دور باشد.", nameof(endedAtUtc));
        }

        if (ActualStartAtUtc.HasValue && endedAtUtc <= ActualStartAtUtc.Value)
        {
            throw new ArgumentException("زمان خاتمه باید بعد از شروع مأموریت باشد.", nameof(endedAtUtc));
        }

        ActualEndAtUtc = endedAtUtc;
        DecisionReason = Required(reason, 1000, nameof(reason));
        Status = MissionStatus.Completed;
        UpdatedAtUtc = nowUtc;
    }

    public void CompleteAutomatically(DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.Running);
        if (nowUtc < PlannedEndAtUtc)
        {
            throw new InvalidOperationException("زمان پایان برنامه‌ریزی‌شده هنوز فرا نرسیده است.");
        }

        ActualEndAtUtc = PlannedEndAtUtc;
        Status = MissionStatus.Completed;
        UpdatedAtUtc = nowUtc;
    }

    public void SetOdometerStart(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "کیلومترشمار نمی‌تواند منفی باشد.");
        }

        if (OdometerEndKm.HasValue && value > OdometerEndKm.Value)
        {
            throw new InvalidOperationException("کیلومترشمار شروع نمی‌تواند از کیلومترشمار پایان بیشتر باشد.");
        }

        OdometerStartKm = value;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetOdometerEnd(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "کیلومترشمار نمی‌تواند منفی باشد.");
        }

        if (!OdometerStartKm.HasValue)
        {
            throw new InvalidOperationException("پیش از ثبت کیلومتر پایان، کیلومتر شروع باید ثبت شود.");
        }

        if (value < OdometerStartKm.Value)
        {
            throw new InvalidOperationException("کیلومترشمار پایان باید مساوی یا بیشتر از کیلومتر شروع باشد.");
        }

        OdometerEndKm = value;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RequestWithdrawal(string reason, DateTimeOffset nowUtc)
    {
        if (Status is not (MissionStatus.Confirmed or MissionStatus.Running))
        {
            throw new InvalidOperationException("فقط مأموریت تأییدشده یا در حال اجرا قابل درخواست انصراف است.");
        }

        StatusBeforeWithdrawal = Status;
        DecisionReason = Required(reason, 1000, nameof(reason));
        Status = MissionStatus.WithdrawalPending;
        UpdatedAtUtc = nowUtc;
    }

    public void ApproveWithdrawal(DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.WithdrawalPending);
        if (StatusBeforeWithdrawal == MissionStatus.Running)
        {
            ActualEndAtUtc ??= nowUtc;
        }

        DecisionReason = DecisionReason ?? "درخواست انصراف تأیید شد.";
        Status = MissionStatus.Cancelled;
        StatusBeforeWithdrawal = null;
        UpdatedAtUtc = nowUtc;
    }

    public void RejectWithdrawal(string reason, DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.WithdrawalPending);
        DecisionReason = Required(reason, 1000, nameof(reason));
        Status = StatusBeforeWithdrawal ?? MissionStatus.Confirmed;
        StatusBeforeWithdrawal = null;
        UpdatedAtUtc = nowUtc;
    }

    public void AutoCloseExpiredWithdrawal(DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.WithdrawalPending);
        if (nowUtc < PlannedEndAtUtc)
        {
            throw new InvalidOperationException("زمان پایان برنامه‌ریزی‌شده هنوز فرا نرسیده است.");
        }

        if (StatusBeforeWithdrawal == MissionStatus.Running)
        {
            ActualEndAtUtc = nowUtc;
        }

        DecisionReason = "درخواست انصراف تا پایان زمان برنامه‌ریزی‌شده تعیین تکلیف نشد.";
        Status = MissionStatus.Cancelled;
        StatusBeforeWithdrawal = null;
        UpdatedAtUtc = nowUtc;
    }

    public void Cancel(string reason, DateTimeOffset nowUtc)
    {
        if (IsFinal)
        {
            throw new InvalidOperationException("مأموریت پایان‌یافته یا لغوشده قابل لغو مجدد نیست.");
        }

        if (Status == MissionStatus.Running || StatusBeforeWithdrawal == MissionStatus.Running)
        {
            ActualEndAtUtc = nowUtc;
        }

        DecisionReason = Required(reason, 1000, nameof(reason));
        Status = MissionStatus.Cancelled;
        StatusBeforeWithdrawal = null;
        UpdatedAtUtc = nowUtc;
    }

    public void EditPendingRequest(MissionRequestChanges changes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(changes);
        EnsureStatus(MissionStatus.Pending);
        ApplyRequestFields(changes);
        UpdatedAtUtc = nowUtc;
    }

    public void CloseFirstSegmentForSplit(
        DateTimeOffset splitAtUtc,
        long odometerEndKm,
        string reason,
        string actor,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(MissionStatus.Running);
        ValidateHour(splitAtUtc, nameof(splitAtUtc));
        if (!ActualStartAtUtc.HasValue
            || splitAtUtc <= ActualStartAtUtc.Value
            || splitAtUtc >= PlannedEndAtUtc
            || splitAtUtc > nowUtc)
        {
            throw new ArgumentException("زمان تقسیم باید پس از شروع و پیش از پایان برنامه‌ریزی‌شده و حداکثر تا زمان جاری باشد.", nameof(splitAtUtc));
        }

        SetOdometerEnd(odometerEndKm);
        ActualEndAtUtc = splitAtUtc;
        PlannedEndAtUtc = splitAtUtc;
        SplitAtUtc = splitAtUtc;
        SplitReason = Required(reason, 1000, nameof(reason));
        SplitBy = Required(actor, 64, nameof(actor));
        DecisionReason = SplitReason;
        Status = MissionStatus.Completed;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkAsSecondSplitSegment(Guid relatedMissionId, DateTimeOffset splitAtUtc, string reason, string actor, DateTimeOffset nowUtc)
    {
        SetRelatedMission(relatedMissionId);
        SplitAtUtc = splitAtUtc;
        SplitReason = Required(reason, 1000, nameof(reason));
        SplitBy = Required(actor, 64, nameof(actor));
        DecisionReason = SplitReason;
        UpdatedAtUtc = nowUtc;
    }

    public void SetRelatedMission(Guid relatedMissionId)
    {
        if (relatedMissionId == Guid.Empty || relatedMissionId == Id)
        {
            throw new ArgumentException("شناسه مأموریت مرتبط معتبر نیست.", nameof(relatedMissionId));
        }

        RelatedMissionId = relatedMissionId;
    }

    /// <summary>تخصیص منابع بخش دوم پس از تقسیم؛ سازگاری زمانی پیش‌تر در سرویس کنترل شده است.</summary>
    public void SetResourcesForSplitSegment(Guid driverId, Guid vehicleId)
    {
        if (driverId == Guid.Empty || vehicleId == Guid.Empty)
        {
            throw new ArgumentException("راننده و خودرو باید مشخص باشند.");
        }

        DriverId = driverId;
        VehicleId = vehicleId;
    }

    public void ApplyApprovedChanges(MissionChanges changes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (Status is not (MissionStatus.Confirmed or MissionStatus.Running))
        {
            throw new InvalidOperationException("درخواست تغییر فقط برای مأموریت تأییدشده یا در حال اجرا قابل اعمال است.");
        }

        ValidateType(changes.Type);
        if (changes.DriverId == Guid.Empty || changes.VehicleId == Guid.Empty)
        {
            throw new ArgumentException("راننده و خودرو باید مشخص باشند.");
        }

        ValidateHour(changes.PlannedStartAtUtc, nameof(changes.PlannedStartAtUtc));
        ValidateHour(changes.PlannedEndAtUtc, nameof(changes.PlannedEndAtUtc));
        if (changes.PlannedEndAtUtc <= changes.PlannedStartAtUtc)
        {
            throw new ArgumentException("زمان پایان باید بعد از زمان شروع باشد.");
        }

        if (ActualStartAtUtc.HasValue && changes.PlannedEndAtUtc <= ActualStartAtUtc.Value)
        {
            throw new ArgumentException("زمان پایان جدید نمی‌تواند پیش از شروع واقعی مأموریت باشد.");
        }

        if (Status == MissionStatus.Running && changes.PlannedStartAtUtc > nowUtc)
        {
            throw new ArgumentException("زمان شروع مأموریت در حال اجرا نمی‌تواند به آینده منتقل شود.");
        }

        if (changes.PassengerCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(changes.PassengerCount), "تعداد همراهان نمی‌تواند منفی باشد.");
        }

        var vehicleChanged = VehicleId != changes.VehicleId;
        if (vehicleChanged && !changes.OdometerStartKm.HasValue)
        {
            throw new InvalidOperationException("برای تغییر خودرو باید کیلومترشمار شروع خودروی جدید مشخص شود.");
        }

        Title = Required(changes.Title, 200, nameof(changes.Title));
        Type = changes.Type;
        Purpose = Required(changes.Purpose, 500, nameof(changes.Purpose));
        Origin = Required(changes.Origin, 200, nameof(changes.Origin));
        Destination = Required(changes.Destination, 200, nameof(changes.Destination));
        RouteJson = JsonSerializer.Serialize(NormalizeStops(changes.IntermediateStops));
        PlannedStartAtUtc = changes.PlannedStartAtUtc;
        PlannedEndAtUtc = changes.PlannedEndAtUtc;
        DriverId = changes.DriverId;
        VehicleId = changes.VehicleId;
        RequesterName = Required(changes.RequesterName, 200, nameof(changes.RequesterName));
        RequesterPhoneSnapshot = Required(changes.RequesterPhoneSnapshot, 20, nameof(changes.RequesterPhoneSnapshot));
        RequesterOrganizationalUnit = Required(changes.RequesterOrganizationalUnit, 200, nameof(changes.RequesterOrganizationalUnit));
        RequestingGroup = Required(changes.RequestingGroup, 200, nameof(changes.RequestingGroup));
        Description = Optional(changes.Description, 2000);
        PassengerCount = changes.PassengerCount;
        MissionOrderNumber = Optional(changes.MissionOrderNumber, 50);
        MissionOrderDate = changes.MissionOrderDate;
        CargoDescription = Optional(changes.CargoDescription, 500);
        ConsigneeName = Optional(changes.ConsigneeName, 200);
        PassengerNames = Optional(changes.PassengerNames, 1000);

        if (vehicleChanged)
        {
            OdometerStartKm = changes.OdometerStartKm;
            OdometerEndKm = null;
        }
        else if (changes.OdometerStartKm.HasValue)
        {
            SetOdometerStart(changes.OdometerStartKm.Value);
        }

        UpdatedAtUtc = nowUtc;
    }

    public static void ValidateHour(DateTimeOffset value, string parameterName)
    {
        var tehranTime = value.ToOffset(TimeSpan.FromMinutes(TehranOffsetMinutes));
        if (tehranTime.Minute != 0 || tehranTime.Second != 0 || tehranTime.Millisecond != 0)
        {
            throw new ArgumentException("زمان مأموریت باید با دقت ساعت کامل به وقت تهران ثبت شود.", parameterName);
        }
    }

    private void ApplyRequestFields(MissionRequestChanges changes)
    {
        ValidateType(changes.Type);
        ValidateHour(changes.PlannedStartAtUtc, nameof(changes.PlannedStartAtUtc));
        ValidateHour(changes.PlannedEndAtUtc, nameof(changes.PlannedEndAtUtc));
        if (changes.PlannedEndAtUtc <= changes.PlannedStartAtUtc)
        {
            throw new ArgumentException("زمان پایان باید بعد از زمان شروع باشد.");
        }

        if (changes.PassengerCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(changes.PassengerCount), "تعداد همراهان نمی‌تواند منفی باشد.");
        }

        Title = Required(changes.Title, 200, nameof(changes.Title));
        RequesterPhoneSnapshot = Required(changes.RequesterPhoneSnapshot, 20, nameof(changes.RequesterPhoneSnapshot));
        RequestingGroup = Required(changes.RequestingGroup, 200, nameof(changes.RequestingGroup));
        Type = changes.Type;
        Purpose = Required(changes.Purpose, 500, nameof(changes.Purpose));
        Origin = Required(changes.Origin, 200, nameof(changes.Origin));
        Destination = Required(changes.Destination, 200, nameof(changes.Destination));
        RouteJson = JsonSerializer.Serialize(NormalizeStops(changes.IntermediateStops));
        PlannedStartAtUtc = changes.PlannedStartAtUtc;
        PlannedEndAtUtc = changes.PlannedEndAtUtc;
        Description = Optional(changes.Description, 2000);
        PassengerCount = changes.PassengerCount;
        MissionOrderNumber = Optional(changes.MissionOrderNumber, 50);
        MissionOrderDate = changes.MissionOrderDate;
        CargoDescription = Optional(changes.CargoDescription, 500);
        ConsigneeName = Optional(changes.ConsigneeName, 200);
        PassengerNames = Optional(changes.PassengerNames, 1000);
    }

    private void EnsureStatus(MissionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException("عملیات با وضعیت فعلی مأموریت سازگار نیست.");
        }
    }

    private static void ValidateType(MissionType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "نوع مأموریت معتبر نیست.");
        }
    }

    private static List<string> NormalizeStops(IEnumerable<string>? stops)
        => (stops ?? [])
            .Where(stop => !string.IsNullOrWhiteSpace(stop))
            .Select(stop => stop.Trim())
            .ToList();

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

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"حداکثر طول مجاز {maxLength} نویسه است.");
        }

        return normalized;
    }
}
