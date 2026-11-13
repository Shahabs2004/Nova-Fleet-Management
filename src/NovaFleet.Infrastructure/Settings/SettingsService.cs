using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Settings;

/// <summary>خواندن/نوشتن تنظیمات عملیاتی و مدیریت دلایل تقسیم؛ مقادیر حساس در این جدول نگهداری نمی‌شوند.</summary>
public sealed class SettingsService(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IRuntimeSettings runtime) : ISettingsService
{
    public async Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default)
    {
        RequireAnySettingsManager();
        return await ReadBundleAsync(cancellationToken);
    }

    public Task<SettingsBundleDto> GetRuntimeBundleAsync(CancellationToken cancellationToken = default)
        => ReadBundleAsync(cancellationToken);

    private async Task<SettingsBundleDto> ReadBundleAsync(CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings.AsNoTracking().ToListAsync(cancellationToken);
        string? Read(string key) => settings.FirstOrDefault(item => item.Key == key)?.Value;

        return new SettingsBundleDto(
            new OrganizationProfileDto(
                Read(SettingKeys.OrganizationName) ?? "ناوگان نوین",
                Read(SettingKeys.OrganizationLogo)),
            new MissionRulesDto(
                ReadInt(Read(SettingKeys.MinimumGapMinutes), SettingKeys.DefaultMinimumGapMinutes),
                ReadInt(Read(SettingKeys.FutureRequestDays), SettingKeys.DefaultFutureRequestDays),
                ReadInt(Read(SettingKeys.WithdrawalAutoCloseHours), SettingKeys.DefaultWithdrawalAutoCloseHours),
                ReadInt(Read(SettingKeys.SmsReminderMinutes), SettingKeys.DefaultSmsReminderMinutes)),
            new SessionPolicyDto(
                ReadInt(Read(SettingKeys.SessionAbsoluteMinutes), SettingKeys.DefaultSessionAbsoluteMinutes),
                ReadInt(Read(SettingKeys.SessionIdleMinutes), SettingKeys.DefaultSessionIdleMinutes)),
            new MissionOrderTemplateDto(
                Read(SettingKeys.MissionOrderHeader),
                Read(SettingKeys.MissionOrderFooter),
                bool.TryParse(Read(SettingKeys.MissionOrderShowLogo), out var showLogo) && showLogo));
    }

    public async Task<OrganizationProfileDto> UpdateOrganizationAsync(
        OrganizationProfileDto profile,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.SystemManage);
        await SettingsWriters.WriteOrganizationAsync(runtime, profile, cancellationToken);
        await RecordAsync("UpdateOrganizationProfile", "SystemSetting", SettingKeys.OrganizationName,
            new { profile.Name }, cancellationToken);
        return profile;
    }

    public async Task<MissionRulesDto> UpdateMissionRulesAsync(
        MissionRulesDto rules,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.SystemManage);
        await SettingsWriters.WriteMissionRulesAsync(runtime, rules, cancellationToken);
        await RecordAsync("UpdateMissionRules", "SystemSetting", null, rules, cancellationToken);
        return rules;
    }

    public async Task<SessionPolicyDto> UpdateSessionPolicyAsync(
        SessionPolicyDto policy,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.SessionsManage);
        await SettingsWriters.WriteSessionPolicyAsync(runtime, policy, cancellationToken);
        await RecordAsync("UpdateSessionPolicy", "SystemSetting", null, policy, cancellationToken);
        return policy;
    }

    public async Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(
        MissionOrderTemplateDto template,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.MissionTemplateManage);
        await SettingsWriters.WriteOrderTemplateAsync(runtime, template, cancellationToken);
        await RecordAsync("UpdateMissionOrderTemplate", "SystemSetting", null, template, cancellationToken);
        return template;
    }

    public async Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        RequireAnySettingsManager();
        var query = db.MissionSplitReasons.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(item => item.IsActive);
        }

        return await query
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Title)
            .Select(item => new SplitReasonDto(item.Id, item.Title, item.SortOrder, item.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.MissionSplitReasonsManage);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ValidationAppException("عنوان دلیل تقسیم الزامی است.");
        }

        var normalized = title.Trim();
        if (await db.MissionSplitReasons.AnyAsync(item => item.Title == normalized, cancellationToken))
        {
            throw new ConflictAppException("این دلیل تقسیم قبلاً ثبت شده است.");
        }

        var nextOrder = await db.MissionSplitReasons.MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? 0;
        var reason = new MissionSplitReason(normalized, nextOrder + 1);
        db.MissionSplitReasons.Add(reason);
        await db.SaveChangesAsync(cancellationToken);
        var created = new SplitReasonDto(reason.Id, reason.Title, reason.SortOrder, reason.IsActive);
        await RecordAsync("CreateSplitReason", "MissionSplitReason", created.Id.ToString(), created, cancellationToken);
        return created;
    }

    public async Task<SplitReasonDto> UpdateSplitReasonAsync(
        Guid id,
        UpdateSplitReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.MissionSplitReasonsManage);
        var reason = await db.MissionSplitReasons.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundAppException("دلیل تقسیم پیدا نشد.");

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationAppException("عنوان دلیل تقسیم الزامی است.");
        }

        var normalized = request.Title.Trim();
        if (await db.MissionSplitReasons.AnyAsync(item => item.Title == normalized && item.Id != id, cancellationToken))
        {
            throw new ConflictAppException("این دلیل تقسیم قبلاً ثبت شده است.");
        }

        var before = new { reason.Title, reason.SortOrder, reason.IsActive };
        reason.Rename(normalized);
        reason.SetSortOrder(request.SortOrder);
        if (request.IsActive)
        {
            reason.Enable();
        }
        else
        {
            reason.Disable();
        }

        await db.SaveChangesAsync(cancellationToken);
        var updated = new SplitReasonDto(reason.Id, reason.Title, reason.SortOrder, reason.IsActive);
        await RecordAsync("UpdateSplitReason", "MissionSplitReason", updated.Id.ToString(),
            updated, cancellationToken, before);
        return updated;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        => await db.SystemSettings.AsNoTracking()
            .Where(item => item.Key == key && !item.IsSecret)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

    private void RequireAnySettingsManager() => currentUser.RequireAny(
        PermissionCatalog.Administration.SystemManage,
        PermissionCatalog.Administration.SessionsManage,
        PermissionCatalog.Administration.MissionTemplateManage,
        PermissionCatalog.Administration.MissionSplitReasonsManage);

    private Task RecordAsync(
        string action,
        string entityType,
        string? entityId,
        object? after,
        CancellationToken cancellationToken,
        object? before = null) =>
        audit.WriteAsync(new AuditRecord(action, entityType, entityId, "Success", Before: before, After: after), cancellationToken);

    private static int ReadInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
