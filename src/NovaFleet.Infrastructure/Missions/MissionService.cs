using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Common;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Vehicles;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Missions;

/// <summary>
/// چرخه عمر مأموریت؛ عملیات حساس (تأیید، تغییر، تقسیم) در تراکنش سریالایز
/// انجام می‌شود و اعلان/پیامک فقط پس از commit و به‌صورت غیرمسدودکننده ثبت می‌گردد.
/// </summary>
public sealed class MissionService(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IClock clock,
    ISettingsService settings,
    INotificationPublisher notifications,
    ISmsDispatcher sms) : IMissionService
{
    private static readonly JsonSerializerOptions ChangeJson = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<MissionListItemDto>> SearchAsync(MissionQuery query, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.View, PermissionCatalog.Missions.ViewOwn);
        var (safePage, safeSize) = Paging.Normalize(query.Page, query.PageSize);

        var source = db.Missions.AsNoTracking();
        if (query.OnlyMine || (!currentUser.Has(PermissionCatalog.Missions.View) && currentUser.Has(PermissionCatalog.Missions.ViewOwn)))
        {
            source = source.Where(item => item.RequesterUsername == currentUser.Username);
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            source = source.Where(item => statuses.Contains(item.Status));
        }

        if (query.Type.HasValue)
        {
            source = source.Where(item => item.Type == query.Type.Value);
        }

        if (query.DriverId.HasValue)
        {
            source = source.Where(item => item.DriverId == query.DriverId.Value);
        }

        if (query.VehicleId.HasValue)
        {
            source = source.Where(item => item.VehicleId == query.VehicleId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.OrganizationalUnit))
        {
            var unit = query.OrganizationalUnit.Trim();
            source = source.Where(item => item.RequesterOrganizationalUnit == unit);
        }

        if (query.FromUtc.HasValue)
        {
            source = source.Where(item => item.PlannedStartAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            source = source.Where(item => item.PlannedStartAtUtc <= query.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(item =>
                item.MissionNumber.Contains(term)
                || item.Title.Contains(term)
                || item.RequesterName.Contains(term)
                || item.Origin.Contains(term)
                || item.Destination.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source
            .OrderByDescending(item => item.PlannedStartAtUtc)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .Select(item => new
            {
                item,
                DriverName = db.Drivers.Where(d => d.Id == item.DriverId).Select(d => d.FirstName + " " + d.LastName).FirstOrDefault(),
                VehiclePlate = db.Vehicles.Where(v => v.Id == item.VehicleId).Select(v => v.PlateDisplay).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new MissionListItemDto(
            row.item.Id,
            row.item.MissionNumber,
            row.item.Title,
            row.item.Type,
            row.item.Status,
            row.item.RequesterName,
            row.item.RequesterOrganizationalUnit,
            row.item.Origin,
            row.item.Destination,
            row.item.PlannedStartAtUtc,
            row.item.PlannedEndAtUtc,
            row.item.ActualEndAtUtc,
            row.item.DriverId,
            row.DriverName,
            row.item.VehicleId,
            row.VehiclePlate,
            row.item.SplitReason,
            row.item.RelatedMissionId,
            row.item.RequesterUsername == currentUser.Username)).ToList();

        return new PagedResult<MissionListItemDto>(items, total, safePage, safeSize);
    }

    public async Task<MissionDetailsDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.View, PermissionCatalog.Missions.ViewOwn);
        var mission = await LoadAsync(id, cancellationToken);
        EnsureVisible(mission);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> CreateAsync(MissionRequestInput input, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Create);
        var now = clock.UtcNow;
        var futureDays = await GetFutureDaysAsync(cancellationToken);
        ValidateRequestWindow(input.PlannedStartAtUtc, input.PlannedEndAtUtc, now, futureDays);

        var user = await db.Users.AsNoTracking()
            .Where(item => item.Username == currentUser.Username)
            .Select(item => new { item.FirstName, item.LastName, item.OrganizationalUnit })
            .FirstOrDefaultAsync(cancellationToken);
        var requesterName = currentUser.IsAuthenticated ? currentUser.DisplayName : $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(requesterName))
        {
            requesterName = currentUser.Username;
        }

        var organizationalUnit = !string.IsNullOrWhiteSpace(input.RequesterOrganizationalUnit)
            ? input.RequesterOrganizationalUnit
            : user?.OrganizationalUnit ?? string.Empty;
        if (string.IsNullOrWhiteSpace(organizationalUnit))
        {
            throw new ValidationAppException("واحد سازمانی درخواست‌کننده مشخص نیست؛ آن را در فرم انتخاب کنید یا در تنظیمات > واحدهای سازمانی تعریف شود.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var missionNumber = await NextMissionNumberAsync(now, cancellationToken);
        var mission = Run(() => new Mission(
            missionNumber,
            input.Title,
            currentUser.Username,
            requesterName,
            input.RequesterPhoneSnapshot,
            organizationalUnit,
            input.RequestingGroup,
            input.Type,
            input.Purpose,
            input.Origin,
            input.Destination,
            input.IntermediateStops,
            input.PlannedStartAtUtc,
            input.PlannedEndAtUtc,
            input.Description,
            input.PassengerCount,
            input.MissionOrderNumber,
            input.MissionOrderDate,
            input.CargoDescription,
            input.ConsigneeName,
            input.PassengerNames));
        db.Missions.Add(mission);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await audit.WriteAsync(new AuditRecord("CreateMission", "Mission", mission.Id.ToString(), "Success", After: new { mission.MissionNumber, mission.Title, mission.PlannedStartAtUtc }), cancellationToken);
        await notifications.PublishToPermissionAsync(
            new NotificationMessage(
                "درخواست مأموریت جدید",
                $"درخواست «{mission.Title}» با شماره {mission.MissionNumber} ثبت شد و منتظر بررسی است.",
                "mission.created",
                "Mission",
                mission.Id.ToString()),
            PermissionCatalog.Missions.Review,
            cancellationToken);

        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> UpdatePendingAsync(Guid id, MissionRequestInput input, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Edit);
        var mission = await LoadAsync(id, cancellationToken);
        if (mission.RequesterUsername != currentUser.Username && !currentUser.Has(PermissionCatalog.Missions.Approve))
        {
            throw new ForbiddenAppException("فقط ثبت‌کننده درخواست می‌تواند آن را ویرایش کند.");
        }

        var now = clock.UtcNow;
        var futureDays = await GetFutureDaysAsync(cancellationToken);
        ValidateRequestWindow(input.PlannedStartAtUtc, input.PlannedEndAtUtc, now, futureDays);

        var before = new { mission.Title, mission.PlannedStartAtUtc, mission.PlannedEndAtUtc, mission.Origin, mission.Destination };
        Run(() => mission.EditPendingRequest(new MissionRequestChanges(
            input.Title,
            input.RequesterPhoneSnapshot,
            input.RequestingGroup,
            input.Type,
            input.Purpose,
            input.Origin,
            input.Destination,
            input.IntermediateStops,
            input.PlannedStartAtUtc,
            input.PlannedEndAtUtc,
            input.Description,
            input.PassengerCount,
            input.MissionOrderNumber,
            input.MissionOrderDate,
            input.CargoDescription,
            input.ConsigneeName,
            input.PassengerNames), now));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("UpdateMissionRequest", "Mission", id.ToString(), "Success", Before: before, After: new { mission.Title, mission.PlannedStartAtUtc, mission.PlannedEndAtUtc }), cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> StartReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Review);
        var mission = await LoadAsync(id, cancellationToken);
        Run(() => mission.StartReview(clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("StartMissionReview", "Mission", id.ToString(), "Success"), cancellationToken);
        await notifications.PublishAsync(
            new NotificationMessage(
                "بررسی درخواست آغاز شد",
                $"بررسی درخواست «{mission.Title}» با شماره {mission.MissionNumber} آغاز شد.",
                "mission.review.started",
                "Mission",
                mission.Id.ToString()),
            [mission.RequesterUsername],
            cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> ApproveAsync(Guid id, MissionApprovalRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Approve);
        var now = clock.UtcNow;
        var gap = await GetGapMinutesAsync(cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var mission = await LoadAsync(id, cancellationToken);
        var driver = await db.Drivers.FirstOrDefaultAsync(item => item.Id == request.DriverId, cancellationToken)
            ?? throw new NotFoundAppException("راننده انتخاب‌شده پیدا نشد.");
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(item => item.Id == request.VehicleId, cancellationToken)
            ?? throw new NotFoundAppException("خودروی انتخاب‌شده پیدا نشد.");

        if (!driver.CanBeAssignedToMission())
        {
            throw new ConflictAppException("راننده انتخاب‌شده فعال نیست یا بایگانی شده است.");
        }

        if (!vehicle.IsReadyForAssignment)
        {
            throw new ConflictAppException("خودروی انتخاب‌شده بایگانی شده یا در تعمیرگاه/خارج از سرویس است.");
        }

        var conflict = await FindConflictAsync(driver.Id, vehicle.Id, mission.PlannedStartAtUtc, mission.PlannedEndAtUtc, gap, mission.Id, cancellationToken);
        if (conflict is not null)
        {
            throw new ConflictAppException($"تخصیص ممکن نیست؛ تداخل با مأموریت {conflict.MissionNumber}. راننده و خودرو باید حداقل {gap} دقیقه فاصله زمانی بین مأموریت‌ها داشته باشند.");
        }

        Run(() => mission.Approve(driver.Id, vehicle.Id, currentUser.Username, now));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await audit.WriteAsync(new AuditRecord("ApproveMission", "Mission", id.ToString(), "Success", Reason: request.Note, After: new { mission.MissionNumber, Driver = driver.DisplayName, Vehicle = vehicle.PlateDisplay }), cancellationToken);
        await NotifyRequesterAsync(mission, "مأموریت تأیید شد", $"مأموریت «{mission.Title}» با شماره {mission.MissionNumber} تأیید و راننده/خودرو تخصیص یافت.", "mission.approved", cancellationToken);
        await sms.QueueForMissionAsync(id, SmsEventType.MissionApproved, request.Note, cancellationToken);
        await sms.QueueForMissionAsync(id, SmsEventType.MissionStartReminder, null, cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> RejectAsync(Guid id, MissionRejectionRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Reject);
        var mission = await LoadAsync(id, cancellationToken);
        Run(() => mission.Reject(request.Reason, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RejectMission", "Mission", id.ToString(), "Success", Reason: request.Reason), cancellationToken);
        await NotifyRequesterAsync(mission, "درخواست مأموریت رد شد", $"درخواست «{mission.Title}» با شماره {mission.MissionNumber} رد شد. علت: {request.Reason}", "mission.rejected", cancellationToken);
        await sms.QueueForMissionAsync(id, SmsEventType.MissionRejected, request.Reason, cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> CancelAsync(Guid id, MissionCancelRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Cancel);
        var mission = await LoadAsync(id, cancellationToken);
        var before = new { mission.Status };
        Run(() => mission.Cancel(request.Reason, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("CancelMission", "Mission", id.ToString(), "Success", Reason: request.Reason, Before: before, After: new { mission.Status }), cancellationToken);
        await NotifyRequesterAsync(mission, "مأموریت لغو شد", $"مأموریت «{mission.Title}» با شماره {mission.MissionNumber} لغو شد. علت: {request.Reason}", "mission.cancelled", cancellationToken);
        await sms.CancelStartReminderAsync(id, cancellationToken);
        await sms.QueueForMissionAsync(id, SmsEventType.MissionCancelled, request.Reason, cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> ExtendAsync(Guid id, MissionExtensionRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Extend);
        var mission = await LoadAsync(id, cancellationToken);
        var gap = await GetGapMinutesAsync(cancellationToken);
        if (mission.DriverId.HasValue && mission.VehicleId.HasValue)
        {
            var conflict = await FindConflictAsync(mission.DriverId.Value, mission.VehicleId.Value, mission.PlannedStartAtUtc, request.NewPlannedEndAtUtc, gap, mission.Id, cancellationToken);
            if (conflict is not null)
            {
                throw new ConflictAppException($"تمدید ممکن نیست؛ تداخل با مأموریت {conflict.MissionNumber}.");
            }
        }

        var before = new { mission.PlannedEndAtUtc };
        Run(() => mission.Extend(request.NewPlannedEndAtUtc, request.Reason, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ExtendMission", "Mission", id.ToString(), "Success", Reason: request.Reason, Before: before, After: new { mission.PlannedEndAtUtc }), cancellationToken);
        await NotifyRequesterAsync(mission, "مأموریت تمدید شد", $"زمان پایان مأموریت {mission.MissionNumber} تمدید شد.", "mission.extended", cancellationToken);
        await sms.QueueForMissionAsync(id, SmsEventType.MissionChanged, request.Reason, cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> EndAsync(Guid id, MissionEndRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.Approve, PermissionCatalog.Missions.Extend);
        var mission = await LoadAsync(id, cancellationToken);
        var now = clock.UtcNow;
        if (request.OdometerEndKm.HasValue)
        {
            Run(() => mission.SetOdometerEnd(request.OdometerEndKm.Value));
        }

        Run(() => mission.EndEarly(request.EndedAtUtc, request.Reason, now));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("EndMission", "Mission", id.ToString(), "Success", Reason: request.Reason, After: new { mission.ActualEndAtUtc, mission.OdometerEndKm }), cancellationToken);
        await NotifyRequesterAsync(mission, "مأموریت خاتمه یافت", $"مأموریت «{mission.Title}» با شماره {mission.MissionNumber} خاتمه یافت.", "mission.ended", cancellationToken);
        await sms.QueueForMissionAsync(id, SmsEventType.MissionCompleted, request.Reason, cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> SetOdometerAsync(Guid id, MissionOdometerRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.OdometerManage);
        var mission = await LoadAsync(id, cancellationToken);
        db.ApplyRowVersion(mission, request.RowVersion);
        if (request.StartKm.HasValue)
        {
            Run(() => mission.SetOdometerStart(request.StartKm.Value));
        }

        if (request.EndKm.HasValue)
        {
            Run(() => mission.SetOdometerEnd(request.EndKm.Value));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("SetMissionOdometer", "Mission", id.ToString(), "Success", After: new { mission.OdometerStartKm, mission.OdometerEndKm }), cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> RequestWithdrawalAsync(Guid id, MissionWithdrawalRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.Create, PermissionCatalog.Missions.Cancel);
        var mission = await LoadAsync(id, cancellationToken);
        if (mission.RequesterUsername != currentUser.Username && !currentUser.Has(PermissionCatalog.Missions.Cancel))
        {
            throw new ForbiddenAppException("فقط ثبت‌کننده مأموریت می‌تواند درخواست انصراف ثبت کند.");
        }

        Run(() => mission.RequestWithdrawal(request.Reason, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RequestWithdrawal", "Mission", id.ToString(), "Success", Reason: request.Reason), cancellationToken);
        await notifications.PublishToPermissionAsync(
            new NotificationMessage(
                "درخواست انصراف از مأموریت",
                $"برای مأموریت {mission.MissionNumber} درخواست انصراف ثبت شد. علت: {request.Reason}",
                "mission.withdrawal.requested",
                "Mission",
                mission.Id.ToString()),
            PermissionCatalog.Missions.WithdrawalReview,
            cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> DecideWithdrawalAsync(Guid id, bool approve, MissionWithdrawalDecision decision, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.WithdrawalReview);
        var mission = await LoadAsync(id, cancellationToken);
        Run(() =>
        {
            if (approve)
            {
                mission.ApproveWithdrawal(clock.UtcNow);
            }
            else
            {
                mission.RejectWithdrawal(decision.Reason ?? "درخواست انصراف رد شد.", clock.UtcNow);
            }
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord(approve ? "ApproveWithdrawal" : "RejectWithdrawal", "Mission", id.ToString(), "Success", Reason: decision.Reason, After: new { mission.Status }), cancellationToken);

        if (approve)
        {
            await NotifyRequesterAsync(mission, "انصراف تأیید شد", $"انصراف از مأموریت {mission.MissionNumber} تأیید و مأموریت لغو شد.", "mission.withdrawal.approved", cancellationToken);
            await sms.CancelStartReminderAsync(id, cancellationToken);
            await sms.QueueForMissionAsync(id, SmsEventType.WithdrawalApproved, decision.Reason, cancellationToken);
        }
        else
        {
            await NotifyRequesterAsync(mission, "انصراف رد شد", $"درخواست انصراف از مأموریت {mission.MissionNumber} رد شد. علت: {decision.Reason}", "mission.withdrawal.rejected", cancellationToken);
            await sms.QueueForMissionAsync(id, SmsEventType.WithdrawalRejected, decision.Reason, cancellationToken);
        }

        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> RequestChangeAsync(Guid id, MissionChangeRequestInput request, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.Create, PermissionCatalog.Missions.Edit);
        var mission = await LoadAsync(id, cancellationToken);
        if (mission.RequesterUsername != currentUser.Username && !currentUser.Has(PermissionCatalog.Missions.ChangeReview))
        {
            throw new ForbiddenAppException("فقط ثبت‌کننده مأموریت می‌تواند درخواست تغییر ثبت کند.");
        }

        if (mission.Status is not (MissionStatus.Confirmed or MissionStatus.Running))
        {
            throw new ConflictAppException("درخواست تغییر فقط برای مأموریت تأییدشده یا در حال اجرا قابل ثبت است.");
        }

        var proposed = request.Proposed;
        var driverMissing = proposed.DriverId is null || proposed.DriverId == Guid.Empty;
        var vehicleMissing = proposed.VehicleId is null || proposed.VehicleId == Guid.Empty;
        if (driverMissing || vehicleMissing)
        {
            var message = driverMissing
                ? "درخواست تغییر باید راننده پیشنهادی را شامل شود."
                : "درخواست تغییر باید خودروی پیشنهادی را شامل شود.";
            throw new ValidationAppException(message);
        }

        var change = new MissionChangeRequest(
            id,
            currentUser.Username,
            request.Reason,
            JsonSerializer.Serialize(proposed, ChangeJson));
        db.MissionChangeRequests.Add(change);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RequestMissionChange", "Mission", id.ToString(), "Success", Reason: request.Reason, After: new { change.Id, ProposedStart = proposed.PlannedStartAtUtc, ProposedEnd = proposed.PlannedEndAtUtc }), cancellationToken);
        await notifications.PublishToPermissionAsync(
            new NotificationMessage(
                "درخواست تغییر مأموریت",
                $"برای مأموریت {mission.MissionNumber} درخواست تغییر ثبت شد. علت: {request.Reason}",
                "mission.change.requested",
                "Mission",
                mission.Id.ToString()),
            PermissionCatalog.Missions.ChangeReview,
            cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> ReviewChangeAsync(Guid id, Guid changeRequestId, bool start, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.ChangeReview);
        var mission = await LoadAsync(id, cancellationToken);
        var change = await LoadChangeAsync(mission, changeRequestId, cancellationToken);
        if (start)
        {
            Run(() => change.StartReview(currentUser.Username, clock.UtcNow));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("StartMissionChangeReview", "MissionChangeRequest", changeRequestId.ToString(), "Success"), cancellationToken);
        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> DecideChangeAsync(Guid id, Guid changeRequestId, bool approve, MissionChangeDecision decision, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.ChangeReview);
        var now = clock.UtcNow;

        Mission mission;
        MissionChangeRequest change;
        var oldDriverId = default(Guid?);
        var oldVehicleId = default(Guid?);
        var oldStart = default(DateTimeOffset);
        MissionChangeInput proposed = null!;

        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            mission = await LoadAsync(id, cancellationToken);
            change = await LoadChangeAsync(mission, changeRequestId, cancellationToken);
            proposed = JsonSerializer.Deserialize<MissionChangeInput>(change.ProposedValuesJson, ChangeJson)
                ?? throw new ValidationAppException("محتوای درخواست تغییر قابل خواندن نیست.");

            if (!approve)
            {
                Run(() => change.Reject(currentUser.Username, decision.Reason ?? "درخواست تغییر رد شد.", now));
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            else
            {
                oldDriverId = mission.DriverId;
                oldVehicleId = mission.VehicleId;
                oldStart = mission.PlannedStartAtUtc;

                var gap = await GetGapMinutesAsync(cancellationToken);
                var conflict = await FindConflictAsync(proposed.DriverId!.Value, proposed.VehicleId!.Value, proposed.PlannedStartAtUtc, proposed.PlannedEndAtUtc, gap, mission.Id, cancellationToken);
                if (conflict is not null)
                {
                    throw new ConflictAppException($"اعمال تغییر ممکن نیست؛ تداخل با مأموریت {conflict.MissionNumber}.");
                }

                var driver = await db.Drivers.FirstOrDefaultAsync(item => item.Id == proposed.DriverId, cancellationToken)
                    ?? throw new NotFoundAppException("راننده پیشنهادی پیدا نشد.");
                var vehicle = await db.Vehicles.FirstOrDefaultAsync(item => item.Id == proposed.VehicleId, cancellationToken)
                    ?? throw new NotFoundAppException("خودروی پیشنهادی پیدا نشد.");
                if (!driver.CanBeAssignedToMission())
                {
                    throw new ConflictAppException("راننده پیشنهادی فعال نیست یا بایگانی شده است.");
                }

                if (!vehicle.IsReadyForAssignment)
                {
                    throw new ConflictAppException("خودروی پیشنهادی بایگانی شده یا در تعمیرگاه/خارج از سرویس است.");
                }

                var changes = new MissionChanges(
                    proposed.Title,
                    proposed.Type,
                    proposed.Purpose,
                    proposed.Origin,
                    proposed.Destination,
                    proposed.IntermediateStops,
                    proposed.PlannedStartAtUtc,
                    proposed.PlannedEndAtUtc,
                    proposed.DriverId.Value,
                    proposed.VehicleId.Value,
                    mission.RequesterName,
                    proposed.RequesterPhoneSnapshot,
                    mission.RequesterOrganizationalUnit,
                    proposed.RequestingGroup,
                    proposed.Description,
                    proposed.PassengerCount,
                    proposed.MissionOrderNumber,
                    proposed.MissionOrderDate,
                    proposed.CargoDescription,
                    proposed.ConsigneeName,
                    proposed.PassengerNames,
                    proposed.OdometerStartKm);
                Run(() => mission.ApplyApprovedChanges(changes, now));
                Run(() => change.Approve(currentUser.Username, decision.Reason, now));
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
        }

        if (!approve)
        {
            await audit.WriteAsync(new AuditRecord("RejectMissionChange", "MissionChangeRequest", changeRequestId.ToString(), "Success", Reason: decision.Reason), cancellationToken);
            await NotifyRequesterAsync(mission, "درخواست تغییر رد شد", $"درخواست تغییر مأموریت {mission.MissionNumber} رد شد. علت: {decision.Reason}", "mission.change.rejected", cancellationToken);
            return await ToDetailsAsync(mission, cancellationToken);
        }

        await audit.WriteAsync(new AuditRecord("ApproveMissionChange", "MissionChangeRequest", changeRequestId.ToString(), "Success", Reason: decision.Reason, After: new { mission.DriverId, mission.VehicleId, mission.PlannedStartAtUtc }), cancellationToken);
        await NotifyRequesterAsync(mission, "تغییر مأموریت اعمال شد", $"درخواست تغییر مأموریت {mission.MissionNumber} تأیید و اعمال شد.", "mission.change.approved", cancellationToken);

        var assignmentChanged = oldDriverId != mission.DriverId || oldVehicleId != mission.VehicleId;
        await sms.QueueForMissionAsync(id, assignmentChanged ? SmsEventType.DriverOrVehicleChanged : SmsEventType.MissionChanged, decision.Reason, cancellationToken);
        if (oldStart != mission.PlannedStartAtUtc)
        {
            await sms.RescheduleStartReminderAsync(id, cancellationToken);
        }

        return await ToDetailsAsync(mission, cancellationToken);
    }

    public async Task<MissionDetailsDto> SplitAsync(Guid id, MissionSplitRequest request, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Split);
        var now = clock.UtcNow;

        Mission first;
        Mission second;
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            first = await LoadAsync(id, cancellationToken);
            var originalEndAtUtc = first.PlannedEndAtUtc;
            var secondDriverId = request.SecondDriverId ?? first.DriverId;
            var secondVehicleId = request.SecondVehicleId ?? first.VehicleId;
            if (secondDriverId is null || secondVehicleId is null)
            {
                throw new ValidationAppException("برای بخش دوم مأموریت، راننده و خودرو باید مشخص باشند.");
            }

            var gap = await GetGapMinutesAsync(cancellationToken);
            var conflict = await FindConflictAsync(secondDriverId.Value, secondVehicleId.Value, request.SplitAtUtc, originalEndAtUtc, gap, first.Id, cancellationToken);
            if (conflict is not null)
            {
                throw new ConflictAppException($"تقسیم ممکن نیست؛ منابع بخش دوم با مأموریت {conflict.MissionNumber} تداخل دارند.");
            }

            var stops = first.GetIntermediateStops();
            Run(() => first.CloseFirstSegmentForSplit(request.SplitAtUtc, request.OdometerEndKm, request.Reason, currentUser.Username, now));

            var secondMissionNumber = await NextMissionNumberAsync(now, cancellationToken);
            second = Run(() => new Mission(
                secondMissionNumber,
                first.Title,
                first.RequesterUsername,
                first.RequesterName,
                first.RequesterPhoneSnapshot,
                first.RequesterOrganizationalUnit,
                first.RequestingGroup,
                first.Type,
                first.Purpose,
                first.Origin,
                first.Destination,
                stops,
                request.SplitAtUtc,
                originalEndAtUtc,
                first.Description,
                first.PassengerCount,
                first.MissionOrderNumber,
                first.MissionOrderDate,
                first.CargoDescription,
                first.ConsigneeName,
                first.PassengerNames));
            Run(() => second.SetResourcesForSplitSegment(secondDriverId.Value, secondVehicleId.Value));
            Run(() => second.SetOdometerStart(request.OdometerEndKm));
            Run(() => second.MarkAsSecondSplitSegment(first.Id, request.SplitAtUtc, request.Reason, currentUser.Username, now));
            Run(() => first.SetRelatedMission(second.Id));
            db.Missions.Add(second);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        await audit.WriteAsync(new AuditRecord("SplitMission", "Mission", id.ToString(), "Success", Reason: request.Reason, After: new { First = first.MissionNumber, Second = second.MissionNumber, request.OdometerEndKm }), cancellationToken);
        await NotifyRequesterAsync(first, "مأموریت تقسیم شد", $"مأموریت {first.MissionNumber} به دو مأموریت {first.MissionNumber} و {second.MissionNumber} تقسیم شد.", "mission.split", cancellationToken);
        return await ToDetailsAsync(first, cancellationToken);
    }

    public async Task<MissionAvailabilityDto> CheckAvailabilityAsync(Guid missionId, Guid driverId, Guid vehicleId, DateTimeOffset startAtUtc, DateTimeOffset endAtUtc, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.Approve, PermissionCatalog.Missions.ChangeReview);
        var gap = await GetGapMinutesAsync(cancellationToken);
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == driverId, cancellationToken);
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == vehicleId, cancellationToken);
        if (driver is null || vehicle is null)
        {
            throw new NotFoundAppException("راننده یا خودروی انتخاب‌شده پیدا نشد.");
        }

        if (!driver.CanBeAssignedToMission())
        {
            return new MissionAvailabilityDto(true, "راننده فعال نیست یا بایگانی شده است.", driver.DisplayName, vehicle.PlateDisplay);
        }

        if (!vehicle.IsReadyForAssignment)
        {
            return new MissionAvailabilityDto(true, "خودرو بایگانی شده یا در تعمیرگاه/خارج از سرویس است.", driver.DisplayName, vehicle.PlateDisplay);
        }

        var conflict = await FindConflictAsync(driverId, vehicleId, startAtUtc, endAtUtc, gap, missionId, cancellationToken);
        return conflict is null
            ? new MissionAvailabilityDto(false, null, driver.DisplayName, vehicle.PlateDisplay)
            : new MissionAvailabilityDto(true, $"تداخل با مأموریت {conflict.MissionNumber}؛ حداقل فاصله {gap} دقیقه لازم است.", driver.DisplayName, vehicle.PlateDisplay);
    }

    public async Task<byte[]> ExportOrderPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.Print);
        var mission = await LoadAsync(id, cancellationToken);
        EnsureVisible(mission);
        if (!mission.ApprovedAtUtc.HasValue)
        {
            throw new ValidationAppException("چاپ حکم فقط پس از تأیید مأموریت امکان‌پذیر است.");
        }

        var details = await ToDetailsAsync(mission, cancellationToken);
        var bundle = await settings.GetRuntimeBundleAsync(cancellationToken);
        return MissionOrderPdf.Build(details, bundle, currentUser.DisplayName, clock.UtcNow);
    }

    private async Task<Mission> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Missions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundAppException("مأموریت پیدا نشد.");

    private async Task<MissionChangeRequest> LoadChangeAsync(Mission mission, Guid changeRequestId, CancellationToken cancellationToken) =>
        await db.MissionChangeRequests.FirstOrDefaultAsync(item => item.Id == changeRequestId && item.MissionId == mission.Id, cancellationToken)
            ?? throw new NotFoundAppException("درخواست تغییر پیدا نشد.");

    private void EnsureVisible(Mission mission)
    {
        if (!currentUser.Has(PermissionCatalog.Missions.View)
            && mission.RequesterUsername != currentUser.Username)
        {
            throw new ForbiddenAppException("این مأموریت متعلق به شما نیست.");
        }
    }

    private async Task<int> GetGapMinutesAsync(CancellationToken cancellationToken)
    {
        var value = await settings.GetValueAsync(SettingKeys.MinimumGapMinutes, cancellationToken);
        return int.TryParse(value, out var minutes) && minutes >= 0 ? minutes : SettingKeys.DefaultMinimumGapMinutes;
    }

    private async Task<int> GetFutureDaysAsync(CancellationToken cancellationToken)
    {
        var value = await settings.GetValueAsync(SettingKeys.FutureRequestDays, cancellationToken);
        return int.TryParse(value, out var days) && days > 0 ? days : SettingKeys.DefaultFutureRequestDays;
    }

    private async Task<string> NextMissionNumberAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var year = JalaliDate.YearOf(nowUtc);
        var sequence = await db.MissionNumberSequences.FirstOrDefaultAsync(item => item.Year == year, cancellationToken);
        if (sequence is null)
        {
            sequence = new MissionNumberSequence(year);
            db.MissionNumberSequences.Add(sequence);
        }

        return MissionNumberSequence.Format(year, sequence.Next());
    }

    private async Task<Mission?> FindConflictAsync(
        Guid driverId,
        Guid vehicleId,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        int gapMinutes,
        Guid excludeMissionId,
        CancellationToken cancellationToken)
    {
        var pad = TimeSpan.FromMinutes(gapMinutes);
        return await db.Missions
            .Where(item => item.Id != excludeMissionId
                && (item.Status == MissionStatus.Confirmed || item.Status == MissionStatus.Running || item.Status == MissionStatus.WithdrawalPending)
                && (item.DriverId == driverId || item.VehicleId == vehicleId)
                && item.PlannedStartAtUtc <= endAtUtc + pad
                && item.PlannedEndAtUtc >= startAtUtc - pad)
            .OrderBy(item => item.PlannedStartAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ValidateRequestWindow(DateTimeOffset startAtUtc, DateTimeOffset endAtUtc, DateTimeOffset nowUtc, int futureDays)
    {
        if (startAtUtc < nowUtc.AddHours(-1))
        {
            throw new ValidationAppException("زمان شروع مأموریت نمی‌تواند در گذشته باشد.");
        }

        if (startAtUtc > nowUtc.AddDays(futureDays) || endAtUtc > nowUtc.AddDays(futureDays))
        {
            throw new ValidationAppException($"زمان مأموریت حداکثر تا {futureDays} روز آینده قابل ثبت است.");
        }
    }

    private async Task NotifyRequesterAsync(Mission mission, string title, string message, string eventKey, CancellationToken cancellationToken) =>
        await notifications.PublishAsync(
            new NotificationMessage(title, message, eventKey, "Mission", mission.Id.ToString()),
            [mission.RequesterUsername],
            cancellationToken);

    private async Task<MissionDetailsDto> ToDetailsAsync(Mission mission, CancellationToken cancellationToken)
    {
        Driver? driver = null;
        if (mission.DriverId.HasValue)
        {
            driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == mission.DriverId, cancellationToken);
        }

        Vehicle? vehicle = null;
        if (mission.VehicleId.HasValue)
        {
            vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == mission.VehicleId, cancellationToken);
        }

        var relatedNumber = mission.RelatedMissionId.HasValue
            ? await db.Missions.AsNoTracking().Where(item => item.Id == mission.RelatedMissionId).Select(item => item.MissionNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

        var changes = await db.MissionChangeRequests.AsNoTracking()
            .Where(item => item.MissionId == mission.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var changeDtos = changes.Select(item => new MissionChangeRequestDto(
            item.Id,
            item.RequesterUsername,
            item.Reason,
            item.Status,
            item.ReviewerUsername,
            item.CreatedAtUtc,
            item.ReviewStartedAtUtc,
            item.ReviewedAtUtc,
            item.DecisionReason,
            JsonSerializer.Deserialize<MissionChangeInput>(item.ProposedValuesJson, ChangeJson) ?? new MissionChangeInput(
                mission.Title,
                mission.Type,
                mission.Purpose,
                mission.Origin,
                [],
                mission.Destination,
                mission.PlannedStartAtUtc,
                mission.PlannedEndAtUtc,
                mission.RequestingGroup,
                mission.RequesterPhoneSnapshot,
                mission.Description,
                mission.PassengerCount,
                mission.MissionOrderNumber,
                mission.MissionOrderDate,
                mission.CargoDescription,
                mission.ConsigneeName,
                mission.PassengerNames,
                mission.DriverId,
                mission.VehicleId,
                mission.OdometerStartKm))).ToList();

        return new MissionDetailsDto(
            mission.Id,
            mission.MissionNumber,
            mission.Title,
            mission.Type,
            mission.Status,
            mission.RequesterUsername,
            mission.RequesterName,
            mission.RequesterPhoneSnapshot,
            mission.RequesterOrganizationalUnit,
            mission.RequestingGroup,
            mission.Purpose,
            mission.Origin,
            mission.GetIntermediateStops(),
            mission.Destination,
            mission.PlannedStartAtUtc,
            mission.PlannedEndAtUtc,
            mission.ActualStartAtUtc,
            mission.ActualEndAtUtc,
            mission.DriverId,
            driver?.DisplayName,
            driver?.MobilePhone,
            mission.VehicleId,
            vehicle?.DisplayName,
            vehicle?.PlateDisplay,
            mission.Description,
            mission.PassengerCount,
            mission.MissionOrderNumber,
            mission.MissionOrderDate,
            mission.CargoDescription,
            mission.ConsigneeName,
            mission.PassengerNames,
            mission.OdometerStartKm,
            mission.OdometerEndKm,
            mission.DistanceKm,
            mission.DecisionReason,
            mission.StatusBeforeWithdrawal,
            mission.RelatedMissionId,
            relatedNumber,
            mission.SplitReason,
            mission.SplitAtUtc,
            mission.SplitBy,
            mission.ApprovedBy,
            mission.ApprovedAtUtc,
            mission.ReviewStartedAtUtc,
            mission.CreatedAtUtc,
            mission.UpdatedAtUtc,
            mission.RowVersion.ToRowVersionString(),
            mission.RequesterUsername == currentUser.Username,
            changeDtos);
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException ex)
        {
            throw new ValidationAppException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictAppException(ex.Message);
        }
    }

    private static T Run<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (ArgumentException ex)
        {
            throw new ValidationAppException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictAppException(ex.Message);
        }
    }
}
