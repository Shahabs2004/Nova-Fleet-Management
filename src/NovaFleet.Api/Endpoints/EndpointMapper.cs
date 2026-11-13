using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NovaFleet.Api.Identity;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Auditing;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Dashboard;
using NovaFleet.Application.Drivers;
using NovaFleet.Application.Identity;
using NovaFleet.Application.Locations;
using NovaFleet.Application.Missions;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Organization;
using NovaFleet.Application.Reports;
using NovaFleet.Application.Sessions;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Setup;
using NovaFleet.Application.Timeline;
using NovaFleet.Application.Vehicles;
using NovaFleet.Domain.Drivers;
using NovaFleet.Domain.Locations;
using NovaFleet.Domain.Missions;
using NovaFleet.Domain.Notifications;
using NovaFleet.Domain.Sessions;
using NovaFleet.Domain.Vehicles;

namespace NovaFleet.Api.Endpoints;

public static class EndpointMapper
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        // Only the bootstrap paths stay anonymous: the status probe must answer before any account exists.
        var bootstrap = app.MapGroup("/api");
        bootstrap.MapSetupEndpoints();
        bootstrap.MapAuthEndpoints();

        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapDashboardEndpoints();
        api.MapDriverEndpoints();
        api.MapVehicleEndpoints();
        api.MapLocationEndpoints();
        api.MapOrganizationEndpoints();
        api.MapMissionEndpoints();
        api.MapNotificationEndpoints();
        api.MapSmsEndpoints();
        api.MapSettingsEndpoints();
        api.MapSecurityEndpoints();
        api.MapReportEndpoints();
        api.MapTimelineEndpoints();
    }

    private static void MapSetupEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/setup/status", async (ISetupService service, CancellationToken ct) => await service.GetStatusAsync(ct));

        // گام دیتابیس بی‌نیاز از هر کاربری است؛ پیش از آن هیچ دیتابیسی برای احراز هویت وجود ندارد.
        group.MapGet("/setup/database", async (ISetupService service, CancellationToken ct) =>
            Results.Ok(await service.GetDatabaseStateAsync(ct)));
        group.MapPost("/setup/database", async (SetupDatabaseRequest request, ISetupService service, CancellationToken ct) =>
            Results.Ok(await service.ConfigureDatabaseAsync(request, ct)));

        group.MapPost("/setup/initialize", async (SetupRequest request, ISetupService service, CancellationToken ct) =>
            Results.Ok(await service.InitializeAsync(request, ct)));
    }

    private static void MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/auth/login-local", async (LoginRequest request, IAuthService auth, HttpContext http, CancellationToken ct) =>
            await LoginAsync(request, auth.LoginLocalAsync, http, ct));
        group.MapPost("/auth/login-ldap", async (LoginRequest request, IAuthService auth, HttpContext http, CancellationToken ct) =>
            await LoginAsync(request, auth.LoginLdapAsync, http, ct));
        group.MapPost("/auth/logout", async (IAuthService auth, HttpContext http, CancellationToken ct) =>
        {
            var sessionKey = http.User.FindFirst(SessionCookieEvents.SessionKeyClaim)?.Value;
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                await auth.LogoutAsync(sessionKey, ct);
            }

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { message = "خروج انجام شد." });
        });
        group.MapGet("/auth/me", (CurrentUserHolder holder) =>
        {
            if (holder.User is ResolvedCurrentUser resolved)
            {
                var snapshot = resolved.Snapshot;
                return Results.Ok(new
                {
                    snapshot.Username,
                    snapshot.DisplayName,
                    snapshot.OrganizationalUnit,
                    snapshot.IsLocalAdmin,
                    snapshot.Roles,
                    Permissions = snapshot.EffectiveCodes
                });
            }

            return Results.Unauthorized();
        });
        group.MapPost("/auth/change-password", async (ChangePasswordBody body, IAuthService auth, CurrentUserHolder holder, CancellationToken ct) =>
        {
            if (!holder.User.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            await auth.ChangePasswordAsync(holder.User.Username, body.CurrentPassword, body.NewPassword, ct);
            return Results.Ok(new { message = "رمز عبور تغییر کرد." });
        });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        Func<LoginRequest, SessionContext, CancellationToken, Task<CurrentUserDto>> login,
        HttpContext http,
        CancellationToken ct)
    {
        var sessionKey = Guid.NewGuid().ToString("N");
        var context = new SessionContext(sessionKey, http.Connection.RemoteIpAddress?.ToString());
        var user = await login(request, context, ct);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(SessionCookieEvents.SessionKeyClaim, sessionKey)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false, IssuedUtc = DateTimeOffset.UtcNow });
        return Results.Ok(user);
    }

    private static void MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", async (IDashboardService service, CancellationToken ct) => await service.GetAsync(ct));
    }

    private static void MapDriverEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/drivers", async (IDriverService service, string? search, bool? includeArchived, int? page, int? pageSize, CancellationToken ct) =>
            await service.SearchAsync(search, includeArchived ?? false, page ?? 1, pageSize ?? 20, ct));
        group.MapGet("/drivers/options", async (IDriverService service, bool? includeInactive, CancellationToken ct) =>
            await service.ListOptionsAsync(includeInactive ?? false, ct));
        group.MapPost("/drivers", async (DriverCreateRequest request, IDriverService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(request, ct)));
        group.MapGet("/drivers/{id:guid}", async (Guid id, IDriverService service, CancellationToken ct) => await service.GetAsync(id, ct));
        group.MapPut("/drivers/{id:guid}", async (Guid id, DriverUpdateRequest request, IDriverService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));
        group.MapPost("/drivers/{id:guid}/employment-status", async (Guid id, EmploymentStatusBody body, IDriverService service, CancellationToken ct) =>
            Results.Ok(await service.SetEmploymentStatusAsync(id, body.Status, body.RowVersion, ct)));
        group.MapPost("/drivers/{id:guid}/default-vehicle", async (Guid id, DefaultVehicleBody body, IDriverService service, CancellationToken ct) =>
            Results.Ok(await service.SetDefaultVehicleAsync(id, body.VehicleId, body.RowVersion, ct)));
        group.MapPost("/drivers/{id:guid}/archive", async (Guid id, ArchiveBody body, IDriverService service, CancellationToken ct) =>
            Results.Ok(await service.SetArchivedAsync(id, body.Archived, ct)));
    }

    private static void MapVehicleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/vehicles", async (IVehicleService service, string? search, bool? includeArchived, int? page, int? pageSize, CancellationToken ct) =>
            await service.SearchAsync(search, includeArchived ?? false, page ?? 1, pageSize ?? 20, ct));
        group.MapGet("/vehicles/options", async (IVehicleService service, bool? includeArchived, CancellationToken ct) =>
            await service.ListOptionsAsync(includeArchived ?? false, ct));
        group.MapPost("/vehicles", async (VehicleCreateRequest request, IVehicleService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(request, ct)));
        group.MapGet("/vehicles/{id:guid}", async (Guid id, IVehicleService service, CancellationToken ct) => await service.GetAsync(id, ct));
        group.MapPut("/vehicles/{id:guid}", async (Guid id, VehicleUpdateRequest request, IVehicleService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));
        group.MapPost("/vehicles/{id:guid}/manual-status", async (Guid id, ManualStatusBody body, IVehicleService service, CancellationToken ct) =>
            Results.Ok(await service.SetManualStatusAsync(id, body.Status, body.RowVersion, ct)));
        group.MapPost("/vehicles/{id:guid}/archive", async (Guid id, ArchiveBody body, IVehicleService service, CancellationToken ct) =>
            Results.Ok(await service.SetArchivedAsync(id, body.Archived, ct)));
        group.MapPost("/vehicles/{id:guid}/driver", async (Guid id, VehicleDriverBody body, IDriverService service, CancellationToken ct) =>
        {
            await service.SetVehicleDriverAsync(id, body.DriverId, body.RowVersion, ct);
            return Results.NoContent();
        });
    }

    private static void MapLocationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/locations", async (ILocationService service, LocationStatus? status, string? search, CancellationToken ct) =>
            await service.ListAsync(status, search, ct));
        group.MapGet("/locations/approved-names", async (ILocationService service, CancellationToken ct) =>
            await service.ListApprovedNamesAsync(ct));
        group.MapPost("/locations", async (NameBody body, ILocationService service, CancellationToken ct) =>
            Results.Ok(await service.ProposeAsync(body.Name, ct)));
        group.MapPost("/locations/{id:guid}/approve", async (Guid id, ILocationService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveAsync(id, ct)));
        group.MapPost("/locations/{id:guid}/reject", async (Guid id, ReasonBody body, ILocationService service, CancellationToken ct) =>
            Results.Ok(await service.RejectAsync(id, body.Reason, ct)));
        group.MapPost("/locations/{id:guid}/disable", async (Guid id, ReasonBody body, ILocationService service, CancellationToken ct) =>
            Results.Ok(await service.DisableAsync(id, body.Reason, ct)));
        group.MapPost("/locations/{id:guid}/reopen", async (Guid id, NameBody body, ILocationService service, CancellationToken ct) =>
            Results.Ok(await service.ReopenAsync(id, body.Name, ct)));
        group.MapDelete("/locations/{id:guid}", async (Guid id, ILocationService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.Ok(new { message = "مکان حذف شد." });
        });
    }

    private static void MapOrganizationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/organization-units", async (IOrganizationUnitService service, CancellationToken ct) => await service.ListAsync(ct));
        group.MapPost("/organization-units", async (NameBody body, IOrganizationUnitService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(body.Name, ct)));
        group.MapPut("/organization-units/{id:guid}", async (Guid id, NameBody body, IOrganizationUnitService service, CancellationToken ct) =>
            Results.Ok(await service.RenameAsync(id, body.Name, ct)));
        group.MapPost("/organization-units/{id:guid}/active", async (Guid id, ActiveBody body, IOrganizationUnitService service, CancellationToken ct) =>
            Results.Ok(await service.SetActiveAsync(id, body.Active, ct)));
    }

    private static void MapMissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/missions", async (IMissionService service, string? search, string? statuses, MissionType? type, Guid? driverId, Guid? vehicleId,
            string? organizationalUnit, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, bool? onlyMine, int? page, int? pageSize, CancellationToken ct) =>
        {
            var query = new MissionQuery(
                search,
                ParseStatuses(statuses),
                type,
                driverId,
                vehicleId,
                organizationalUnit,
                fromUtc,
                toUtc,
                onlyMine ?? false,
                page ?? 1,
                pageSize ?? 20);
            return await service.SearchAsync(query, ct);
        });
        group.MapPost("/missions", async (MissionRequestInput input, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(input, ct)));
        group.MapGet("/missions/{id:guid}", async (Guid id, IMissionService service, CancellationToken ct) => await service.GetAsync(id, ct));
        group.MapPut("/missions/{id:guid}", async (Guid id, MissionRequestInput input, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.UpdatePendingAsync(id, input, ct)));
        group.MapPost("/missions/{id:guid}/review", async (Guid id, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.StartReviewAsync(id, ct)));
        group.MapPost("/missions/{id:guid}/approve", async (Guid id, MissionApprovalRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.ApproveAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/reject", async (Guid id, MissionRejectionRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.RejectAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/cancel", async (Guid id, MissionCancelRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.CancelAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/extend", async (Guid id, MissionExtensionRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.ExtendAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/end", async (Guid id, MissionEndRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.EndAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/odometer", async (Guid id, MissionOdometerRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.SetOdometerAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/withdrawal", async (Guid id, MissionWithdrawalRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.RequestWithdrawalAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/withdrawal/decision", async (Guid id, WithdrawalDecisionBody body, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.DecideWithdrawalAsync(id, body.Approve, new MissionWithdrawalDecision(body.Reason), ct)));
        group.MapPost("/missions/{id:guid}/change-requests", async (Guid id, MissionChangeRequestInput request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.RequestChangeAsync(id, request, ct)));
        group.MapPost("/missions/{id:guid}/change-requests/{changeRequestId:guid}/review", async (Guid id, Guid changeRequestId, StartBody body, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewChangeAsync(id, changeRequestId, body.Start, ct)));
        group.MapPost("/missions/{id:guid}/change-requests/{changeRequestId:guid}/decision", async (Guid id, Guid changeRequestId, ChangeDecisionBody body, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.DecideChangeAsync(id, changeRequestId, body.Approve, new MissionChangeDecision(body.Reason), ct)));
        group.MapPost("/missions/{id:guid}/split", async (Guid id, MissionSplitRequest request, IMissionService service, CancellationToken ct) =>
            Results.Ok(await service.SplitAsync(id, request, ct)));
        group.MapGet("/missions/{id:guid}/availability", async (Guid id, Guid driverId, Guid vehicleId, DateTimeOffset startAtUtc, DateTimeOffset endAtUtc, IMissionService service, CancellationToken ct) =>
            await service.CheckAvailabilityAsync(id, driverId, vehicleId, startAtUtc, endAtUtc, ct));
        group.MapGet("/missions/{id:guid}/order.pdf", async (Guid id, IMissionService service, CancellationToken ct) =>
            Results.File(await service.ExportOrderPdfAsync(id, ct), "application/pdf"));
    }

    private static void MapNotificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/notifications", async (INotificationService service, bool? unreadOnly, int? page, int? pageSize, CancellationToken ct) =>
            await service.ListAsync(unreadOnly ?? false, page ?? 1, pageSize ?? 20, ct));
        group.MapGet("/notifications/recent", async (INotificationService service, int? count, CancellationToken ct) =>
            await service.ListRecentAsync(Math.Clamp(count ?? 10, 1, 50), ct));
        group.MapGet("/notifications/unread-count", async (INotificationService service, CancellationToken ct) =>
            Results.Ok(new { count = await service.UnreadCountAsync(ct) }));
        group.MapPost("/notifications/{id:guid}/read", async (Guid id, INotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.Ok(new { message = "اعلان خوانده شد." });
        });
        group.MapPost("/notifications/read-all", async (INotificationService service, CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(ct);
            return Results.Ok(new { message = "همه اعلان‌ها خوانده شدند." });
        });
    }

    private static void MapSmsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/sms", async (ISmsService service, string? search, SmsQueueStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, CancellationToken ct) =>
            await service.ListAsync(new SmsQuery(search, status, fromUtc, toUtc, page ?? 1, pageSize ?? 20), ct));
        group.MapGet("/sms/summary", async (ISmsService service, CancellationToken ct) => await service.GetStatusSummaryAsync(ct));
        group.MapGet("/sms/templates", async (ISmsService service, CancellationToken ct) => await service.ListTemplatesAsync(ct));
        group.MapPut("/sms/templates/{eventType:int}", async (SmsEventType eventType, UpdateSmsTemplateRequest request, ISmsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateTemplateAsync(eventType, request, ct)));
        group.MapPost("/sms/{id:guid}/retry", async (Guid id, ISmsService service, CancellationToken ct) =>
        {
            await service.RetryAsync(id, ct);
            return Results.Ok(new { message = "ارسال مجدد در صف قرار گرفت." });
        });
        group.MapPost("/sms/test", async (SmsTestRequest request, ISmsService service, CancellationToken ct) =>
            Results.Ok(new { id = await service.QueueTestAsync(request, ct) }));
    }

    private static void MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/settings", async (ISettingsService service, CancellationToken ct) => await service.GetBundleAsync(ct));
        group.MapPut("/settings/organization", async (OrganizationProfileDto profile, ISettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateOrganizationAsync(profile, ct)));
        group.MapPut("/settings/mission-rules", async (MissionRulesDto rules, ISettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateMissionRulesAsync(rules, ct)));
        group.MapPut("/settings/session-policy", async (SessionPolicyDto policy, ISettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateSessionPolicyAsync(policy, ct)));
        group.MapPut("/settings/mission-order-template", async (MissionOrderTemplateDto template, ISettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateMissionOrderTemplateAsync(template, ct)));
        group.MapGet("/settings/split-reasons", async (ISettingsService service, bool? includeInactive, CancellationToken ct) =>
            await service.ListSplitReasonsAsync(includeInactive ?? false, ct));
        group.MapPost("/settings/split-reasons", async (NameBody body, ISettingsService service, CancellationToken ct) =>
            Results.Ok(await service.CreateSplitReasonAsync(body.Name, ct)));
        group.MapPut("/settings/split-reasons/{id:guid}", async (Guid id, UpdateSplitReasonRequest request, ISettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateSplitReasonAsync(id, request, ct)));
        group.MapGet("/permissions", () => Results.Ok(PermissionCatalog.Definitions));

        group.MapGet("/settings/connections/directory", async (IConnectionsSettingsService service, CancellationToken ct) =>
            await service.GetDirectoryAsync(ct));
        group.MapPut("/settings/connections/directory", async (UpdateDirectorySettingsDto request, IConnectionsSettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateDirectoryAsync(request, ct)));
        group.MapGet("/settings/connections/sms", async (IConnectionsSettingsService service, CancellationToken ct) =>
            await service.GetSmsAsync(ct));
        group.MapPut("/settings/connections/sms", async (UpdateSmsConnectionSettingsDto request, IConnectionsSettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateSmsAsync(request, ct)));
        group.MapGet("/settings/connections/operations", async (IConnectionsSettingsService service, CancellationToken ct) =>
            await service.GetOperationsAsync(ct));
        group.MapPut("/settings/connections/operations", async (UpdateOperationsSettingsDto request, IConnectionsSettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateOperationsAsync(request, ct)));
        group.MapGet("/settings/connections/database", async (IConnectionsSettingsService service, CancellationToken ct) =>
            await service.GetDatabaseAsync(ct));
        group.MapPut("/settings/connections/database", async (DatabaseSettingsDto request, IConnectionsSettingsService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateDatabaseAsync(request, ct)));

        group.MapPost("/settings/connections/database/test", async (IConnectionTestsService service, CancellationToken ct) =>
            Results.Ok(await service.TestDatabaseAsync(ct)));
        group.MapPost("/settings/connections/directory/test", async (string? sampleUsername, IConnectionTestsService service, CancellationToken ct) =>
            Results.Ok(await service.TestDirectoryAsync(sampleUsername, ct)));
        group.MapPost("/settings/connections/sms/test", async (IConnectionTestsService service, CancellationToken ct) =>
            Results.Ok(await service.TestSmsBalanceAsync(ct)));
    }

    private static void MapSecurityEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/sessions", async (ISessionService service, string? username, UserSessionStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, CancellationToken ct) =>
            await service.ListAsync(new SessionQuery(username, status, fromUtc, toUtc, page ?? 1, pageSize ?? 20), ct));
        group.MapPost("/sessions/{id:guid}/force-end", async (Guid id, ISessionService service, CancellationToken ct) =>
        {
            await service.ForceEndAsync(id, ct);
            return Results.Ok(new { message = "نشست پایان یافت." });
        });
        group.MapGet("/audit", async (IAuditQueryService service, string? actor, string? action, string? entityType, string? outcome, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, CancellationToken ct) =>
            await service.ListAsync(new AuditQuery(actor, action, entityType, outcome, fromUtc, toUtc, page ?? 1, pageSize ?? 20), ct));
        group.MapGet("/audit/actions", async (IAuditQueryService service, CancellationToken ct) => await service.ListActionsAsync(ct));
        group.MapGet("/users", async (IUserService service, string? search, bool? includeDisabled, CancellationToken ct) =>
            await service.ListAsync(search, includeDisabled ?? false, ct));
        group.MapGet("/users/{id:guid}", async (Guid id, IUserService service, CancellationToken ct) => await service.GetAsync(id, ct));
        group.MapPost("/users", async (CreateLocalUserRequest request, IUserService service, CancellationToken ct) =>
            Results.Ok(await service.CreateLocalAsync(request, ct)));
        group.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest request, IUserService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));
        group.MapPost("/users/{id:guid}/disabled", async (Guid id, ActiveBody body, IUserService service, CancellationToken ct) =>
            Results.Ok(await service.SetDisabledAsync(id, body.Active, ct)));
        group.MapPost("/users/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request, IUserService service, CancellationToken ct) =>
        {
            await service.ResetPasswordAsync(id, request, ct);
            return Results.Ok(new { message = "رمز عبور بازنشانی شد." });
        });
        group.MapPut("/users/{id:guid}/permissions", async (Guid id, SetPermissionsRequest request, IUserService service, CancellationToken ct) =>
        {
            await service.SetPermissionsAsync(id, request, ct);
            return Results.Ok(new { message = "مجوزهای کاربر ذخیره شد." });
        });
        group.MapGet("/roles", async (IRoleService service, CancellationToken ct) => await service.ListAsync(ct));
        group.MapPost("/roles", async (SaveRoleRequest request, IRoleService service, CancellationToken ct) =>
            Results.Ok(await service.CreateAsync(request, ct)));
        group.MapPut("/roles/{id:guid}", async (Guid id, SaveRoleRequest request, IRoleService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)));
        group.MapPost("/roles/{id:guid}/active", async (Guid id, ActiveBody body, IRoleService service, CancellationToken ct) =>
            Results.Ok(await service.SetActiveAsync(id, body.Active, ct)));
        group.MapPut("/roles/{id:guid}/permissions", async (Guid id, SetPermissionsRequest request, IRoleService service, CancellationToken ct) =>
        {
            await service.SetPermissionsAsync(id, request, ct);
            return Results.Ok(new { message = "مجوزهای نقش ذخیره شد." });
        });
        group.MapGet("/roles/directory-mappings", async (IRoleService service, CancellationToken ct) => await service.ListDirectoryMappingsAsync(ct));
        group.MapPost("/roles/directory-mappings", async (SaveDirectoryMappingRequest request, IRoleService service, CancellationToken ct) =>
            Results.Ok(await service.SaveDirectoryMappingAsync(request, ct)));
        group.MapDelete("/roles/directory-mappings/{id:guid}", async (Guid id, IRoleService service, CancellationToken ct) =>
        {
            await service.RemoveDirectoryMappingAsync(id, ct);
            return Results.Ok(new { message = "نگاشت حذف شد." });
        });
    }

    private static void MapReportEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/reports", (IReportService service) => Results.Ok(service.GetDefinitions()));
        group.MapPost("/reports/{key}/run", async (string key, ReportQuery query, IReportService service, CancellationToken ct) =>
            Results.Ok(await service.RunAsync(key, query, ct)));
        group.MapPost("/reports/{key}/excel", async (string key, ReportQuery query, IReportService service, CancellationToken ct) =>
            Results.File(
                await service.ExportExcelAsync(key, query, ct),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{key}.xlsx"));
        group.MapPost("/reports/{key}/pdf", async (string key, ReportQuery query, IReportService service, CancellationToken ct) =>
            Results.File(
                await service.ExportPdfAsync(key, query, ct),
                "application/pdf",
                $"{key}.pdf"));
    }

    private static void MapTimelineEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/timeline", async (ITimelineService service, DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? driverId, Guid? vehicleId, CancellationToken ct) =>
            await service.GetAsync(fromUtc, toUtc, driverId, vehicleId, ct));
    }

    private static IReadOnlyList<MissionStatus>? ParseStatuses(string? statuses)
    {
        if (string.IsNullOrWhiteSpace(statuses))
        {
            return null;
        }

        var result = new List<MissionStatus>();
        foreach (var part in statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var value) && Enum.IsDefined((MissionStatus)value))
            {
                result.Add((MissionStatus)value);
            }
        }

        return result.Count > 0 ? result : null;
    }

    public sealed record ChangePasswordBody(string CurrentPassword, string NewPassword);

    public sealed record EmploymentStatusBody(DriverEmploymentStatus Status, string? RowVersion);

    public sealed record DefaultVehicleBody(Guid? VehicleId, string? RowVersion);

    public sealed record VehicleDriverBody(Guid? DriverId, string? RowVersion);

    public sealed record ManualStatusBody(ManualVehicleStatus Status, string? RowVersion);

    public sealed record ArchiveBody(bool Archived);

    public sealed record NameBody(string Name);

    public sealed record ReasonBody(string Reason);

    public sealed record ActiveBody(bool Active);

    public sealed record StartBody(bool Start);

    public sealed record WithdrawalDecisionBody(bool Approve, string? Reason);

    public sealed record ChangeDecisionBody(bool Approve, string? Reason);
}
