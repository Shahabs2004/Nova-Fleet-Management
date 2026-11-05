using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Application.Setup;
using NovaFleet.Domain.Auditing;
using NovaFleet.Domain.Configuration;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Setup;

/// <summary>
/// راه‌اندازی اولیه سامانه؛ تا زمانی که هیچ کاربری وجود نداشته باشد در دسترس است.
/// پس از ساخت مدیر محلی، فراخوانی دوباره آن همیشه رد می‌شود.
/// </summary>
public sealed class SetupService(
    NovaFleetDbContext db,
    IPasswordHasher passwordHasher) : ISetupService
{
    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var userCount = await db.Users.CountAsync(cancellationToken);
        var hasAdmin = await db.Users.AnyAsync(item => item.IsLocalAdmin, cancellationToken);
        var organization = await db.SystemSettings
            .Where(item => item.Key == SettingKeys.OrganizationName)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var completed = await db.SystemSettings
            .AnyAsync(item => item.Key == SettingKeys.SetupCompleted && item.Value == bool.TrueString, cancellationToken);

        return new SetupStatusDto(completed || userCount > 0 && hasAdmin, hasAdmin, userCount, organization);
    }

    public async Task<SetupStatusDto> InitializeAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw new ConflictAppException("راه‌اندازی اولیه قبلاً انجام شده است.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrganizationName, nameof(request.OrganizationName));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AdminUsername, nameof(request.AdminUsername));
        if (request.AdminPassword.Length < 8)
        {
            throw new ValidationAppException("رمز مدیر محلی باید حداقل ۸ نویسه باشد.");
        }

        var admin = new ApplicationUser(
            request.AdminUsername,
            request.AdminFirstName,
            request.AdminLastName,
            isLocalAdmin: true);
        admin.SetPasswordHash(passwordHasher.Hash(request.AdminPassword));
        admin.UpdateProfile(request.AdminFirstName, request.AdminLastName, request.AdminMobile, null, null);
        db.Users.Add(admin);

        db.SystemSettings.Add(new SystemSetting(SettingKeys.OrganizationName, request.OrganizationName.Trim()));
        db.SystemSettings.Add(new SystemSetting(SettingKeys.SetupCompleted, bool.TrueString));
        db.AuditEntries.Add(new AuditEntry(admin.Username, "Setup", "System", null, "Success", afterJson: null, clientIpAddress: null));
        await db.SaveChangesAsync(cancellationToken);

        return await GetStatusAsync(cancellationToken);
    }
}
