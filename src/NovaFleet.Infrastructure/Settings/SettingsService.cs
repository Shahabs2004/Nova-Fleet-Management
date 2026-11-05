using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Missions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Settings;

/// <summary>خواندن/نوشتن تنظیمات عملیاتی و مدیریت دلایل تقسیم؛ مقادیر حساس در این جدول نگهداری نمی‌شوند.</summary>
public sealed class SettingsService(NovaFleetDbContext db, IClock clock) : ISettingsService
{
    public async Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default)
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
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name, nameof(profile.Name));
        await SetAsync(SettingKeys.OrganizationName, profile.Name.Trim(), cancellationToken);
        await SetAsync(SettingKeys.OrganizationLogo, profile.LogoDataUrl ?? string.Empty, cancellationToken);
        return profile;
    }

    public async Task<MissionRulesDto> UpdateMissionRulesAsync(
        MissionRulesDto rules,
        CancellationToken cancellationToken = default)
    {
        if (rules.MinimumGapMinutes is < 0 or > 240)
        {
            throw new ValidationAppException("فاصله اجباری باید بین ۰ تا ۲۴۰ دقیقه باشد.");
        }

        if (rules.FutureRequestDays is < 1 or > 365)
        {
            throw new ValidationAppException("حداکثر بازه آینده درخواست باید بین ۱ تا ۳۶۵ روز باشد.");
        }

        if (rules.WithdrawalAutoCloseHours is < 1 or > 168)
        {
            throw new ValidationAppException("مهلت بسته‌شدن خودکار انصراف باید بین ۱ تا ۱۶۸ ساعت باشد.");
        }

        if (rules.StartReminderMinutes is < 0 or > 1440)
        {
            throw new ValidationAppException("زمان یادآوری پیامک باید بین ۰ تا ۱۴۴۰ دقیقه باشد.");
        }

        await SetAsync(SettingKeys.MinimumGapMinutes, rules.MinimumGapMinutes.ToString(), cancellationToken);
        await SetAsync(SettingKeys.FutureRequestDays, rules.FutureRequestDays.ToString(), cancellationToken);
        await SetAsync(SettingKeys.WithdrawalAutoCloseHours, rules.WithdrawalAutoCloseHours.ToString(), cancellationToken);
        await SetAsync(SettingKeys.SmsReminderMinutes, rules.StartReminderMinutes.ToString(), cancellationToken);
        return rules;
    }

    public async Task<SessionPolicyDto> UpdateSessionPolicyAsync(
        SessionPolicyDto policy,
        CancellationToken cancellationToken = default)
    {
        if (policy.AbsoluteMinutes is < 15 or > 4320 || policy.IdleMinutes is < 5 or > 1440)
        {
            throw new ValidationAppException("بازه عمر یا عدم فعالیت نشست معتبر نیست.");
        }

        await SetAsync(SettingKeys.SessionAbsoluteMinutes, policy.AbsoluteMinutes.ToString(), cancellationToken);
        await SetAsync(SettingKeys.SessionIdleMinutes, policy.IdleMinutes.ToString(), cancellationToken);
        return policy;
    }

    public async Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(
        MissionOrderTemplateDto template,
        CancellationToken cancellationToken = default)
    {
        await SetAsync(SettingKeys.MissionOrderHeader, template.HeaderText ?? string.Empty, cancellationToken);
        await SetAsync(SettingKeys.MissionOrderFooter, template.FooterText ?? string.Empty, cancellationToken);
        await SetAsync(SettingKeys.MissionOrderShowLogo, template.ShowLogo.ToString(), cancellationToken);
        return template;
    }

    public async Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
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
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        var normalized = title.Trim();
        if (await db.MissionSplitReasons.AnyAsync(item => item.Title == normalized, cancellationToken))
        {
            throw new ConflictAppException("این دلیل تقسیم قبلاً ثبت شده است.");
        }

        var nextOrder = await db.MissionSplitReasons.MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? 0;
        var reason = new MissionSplitReason(normalized, nextOrder + 1);
        db.MissionSplitReasons.Add(reason);
        await db.SaveChangesAsync(cancellationToken);
        return new SplitReasonDto(reason.Id, reason.Title, reason.SortOrder, reason.IsActive);
    }

    public async Task<SplitReasonDto> UpdateSplitReasonAsync(
        Guid id,
        UpdateSplitReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        var reason = await db.MissionSplitReasons.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundAppException("دلیل تقسیم پیدا نشد.");

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title, nameof(request.Title));
        var normalized = request.Title.Trim();
        if (await db.MissionSplitReasons.AnyAsync(item => item.Title == normalized && item.Id != id, cancellationToken))
        {
            throw new ConflictAppException("این دلیل تقسیم قبلاً ثبت شده است.");
        }

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
        return new SplitReasonDto(reason.Id, reason.Title, reason.SortOrder, reason.IsActive);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        => await db.SystemSettings.AsNoTracking()
            .Where(item => item.Key == key)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting(key, value));
        }
        else
        {
            setting.SetValue(value, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static int ReadInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;
}
