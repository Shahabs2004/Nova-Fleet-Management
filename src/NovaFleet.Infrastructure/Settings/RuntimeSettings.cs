using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Domain.Configuration;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;

namespace NovaFleet.Infrastructure.Settings;

/// <summary>
/// تنها نقطه نوشتن به جدول تنظیمات. سطرهای سرّی از خواندن‌های معمولی حذف می‌شوند تا
/// هیچ مسیری نتواند بدون درخواست صریح GetSecretAsync به مقدار سرّی دست پیدا کند.
/// </summary>
public sealed class RuntimeSettings(
    NovaFleetDbContext db,
    IClock clock,
    ISecretProtector protector) : IRuntimeSettings
{
    public async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(item => !item.IsSecret)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
        => await db.SystemSettings.AsNoTracking()
            .Where(item => item.Key == key && !item.IsSecret)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default)
        => int.TryParse(await GetStringAsync(key, cancellationToken), out var value) ? value : fallback;

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default)
        => bool.TryParse(await GetStringAsync(key, cancellationToken), out var value) ? value : fallback;

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        => protector.Unprotect(await ReadProtectedAsync(key, cancellationToken));

    public async Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default)
        => await ReadProtectedAsync(key, cancellationToken) is not null;

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var setting = await LoadAsync(key, cancellationToken);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting(key, value ?? string.Empty));
        }
        else
        {
            setting.SetValue(value ?? string.Empty, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetSecretAsync(string key, string? plaintext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return;
        }

        var setting = await LoadAsync(key, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting(key, string.Empty);
            db.SystemSettings.Add(setting);
        }

        setting.SetProtectedValue(protector.Protect(plaintext.Trim()), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await LoadAsync(key, cancellationToken);
        if (setting is null)
        {
            return;
        }

        setting.SetValue(string.Empty, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ReadProtectedAsync(string key, CancellationToken cancellationToken) =>
        await db.SystemSettings.AsNoTracking()
            .Where(item => item.Key == key && item.IsSecret)
            .Select(item => item.ProtectedValue)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<SystemSetting?> LoadAsync(string key, CancellationToken cancellationToken) =>
        await db.SystemSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
}
