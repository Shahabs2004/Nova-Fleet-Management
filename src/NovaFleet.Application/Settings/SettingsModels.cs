namespace NovaFleet.Application.Settings;

public sealed record OrganizationProfileDto(string Name, string? LogoDataUrl);

public sealed record MissionRulesDto(
    int MinimumGapMinutes,
    int FutureRequestDays,
    int WithdrawalAutoCloseHours,
    int StartReminderMinutes);

public sealed record SessionPolicyDto(int AbsoluteMinutes, int IdleMinutes);

public sealed record SplitReasonDto(Guid Id, string Title, int SortOrder, bool IsActive);

public sealed record MissionOrderTemplateDto(string? HeaderText, string? FooterText, bool ShowLogo);

public sealed record SettingsBundleDto(
    OrganizationProfileDto Organization,
    MissionRulesDto MissionRules,
    SessionPolicyDto SessionPolicy,
    MissionOrderTemplateDto MissionOrderTemplate);

public sealed record UpdateSplitReasonRequest(string Title, int SortOrder, bool IsActive);

public interface ISettingsService
{
    Task<SettingsBundleDto> GetBundleAsync(CancellationToken cancellationToken = default);

    Task<OrganizationProfileDto> UpdateOrganizationAsync(OrganizationProfileDto profile, CancellationToken cancellationToken = default);

    Task<MissionRulesDto> UpdateMissionRulesAsync(MissionRulesDto rules, CancellationToken cancellationToken = default);

    Task<SessionPolicyDto> UpdateSessionPolicyAsync(SessionPolicyDto policy, CancellationToken cancellationToken = default);

    Task<MissionOrderTemplateDto> UpdateMissionOrderTemplateAsync(MissionOrderTemplateDto template, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SplitReasonDto>> ListSplitReasonsAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<SplitReasonDto> CreateSplitReasonAsync(string title, CancellationToken cancellationToken = default);

    Task<SplitReasonDto> UpdateSplitReasonAsync(Guid id, UpdateSplitReasonRequest request, CancellationToken cancellationToken = default);

    /// <summary>خواندن تنظیمات کلید/مقدار برای سرویس‌های داخلی.</summary>
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
}
