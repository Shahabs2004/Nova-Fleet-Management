namespace NovaFleet.Application.Setup;

public sealed record SetupStatusDto(bool IsCompleted, bool HasLocalAdmin, int UserCount, string? OrganizationName);

public sealed record SetupRequest(
    string OrganizationName,
    string AdminUsername,
    string AdminPassword,
    string AdminFirstName,
    string AdminLastName,
    string AdminMobile);

/// <summary>
/// راه‌اندازی اولیه؛ تنها زمانی مجاز است که هیچ کاربر محلی فعالی وجود نداشته باشد.
/// هیچ رمز پیش‌فرضی در کد یا پایگاه داده قرار نمی‌گیرد.
/// </summary>
public interface ISetupService
{
    Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SetupStatusDto> InitializeAsync(SetupRequest request, CancellationToken cancellationToken = default);
}
