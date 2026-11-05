using NovaFleet.Application.Common;
using NovaFleet.Domain.Sessions;

namespace NovaFleet.Application.Sessions;

public sealed record UserSessionDto(
    Guid Id,
    string Username,
    string DisplayName,
    UserSessionLoginMethod LoginMethod,
    UserSessionStatus Status,
    DateTimeOffset LoginAtUtc,
    DateTimeOffset LastActivityAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? ClientIpAddress,
    string? Reason);

public sealed record SessionQuery(
    string? Username,
    UserSessionStatus? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);

public interface ISessionService
{
    Task<PagedResult<UserSessionDto>> ListAsync(SessionQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// اعتبارسنجی نشست در هر درخواست؛ نشست منقضی/پایان‌یافته نامعتبر است.
    /// آخرین فعالیت فقط با فاصله زمانی معقول به‌روزرسانی می‌شود تا هر درخواست یک نوشتن ایجاد نکند.
    /// </summary>
    Task<bool> ValidateAsync(string sessionKey, int absoluteMinutes, int idleMinutes, CancellationToken cancellationToken = default);

    /// <summary>پایان اجباری نشست توسط مدیر؛ کاربر در درخواست بعدی خارج می‌شود.</summary>
    Task ForceEndAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> ActiveCountAsync(CancellationToken cancellationToken = default);
}
