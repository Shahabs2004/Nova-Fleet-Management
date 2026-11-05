using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Auditing;
using NovaFleet.Domain.Identity;
using NovaFleet.Domain.Sessions;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Identity;

/// <summary>ورود محلی، دامنه و ویندوز؛ ایجاد نشست و ثبت سابقه موفق/ناموفق.</summary>
public sealed class AuthService(
    NovaFleetDbContext db,
    IPasswordHasher passwordHasher,
    IDirectoryAuthenticator directory,
    IPermissionResolver permissionResolver,
    IClock clock) : IAuthService
{
    private const string InvalidCredentials = "نام کاربری یا رمز عبور معتبر نیست.";

    public async Task<CurrentUserDto> LoginLocalAsync(
        LoginRequest request,
        SessionContext session,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Username, nameof(request.Username));
        var username = request.Username.Trim();
        var user = await db.Users.FirstOrDefaultAsync(item => item.Username == username, cancellationToken);

        if (user is null
            || user.IsDirectoryUser
            || string.IsNullOrEmpty(user.PasswordHash)
            || !passwordHasher.Verify(user.PasswordHash, request.Password ?? string.Empty))
        {
            await RecordFailureAsync(username, UserSessionLoginMethod.Local, session, InvalidCredentials, cancellationToken);
            throw new UnauthorizedAppException(InvalidCredentials);
        }

        if (user.IsDisabled)
        {
            await RecordFailureAsync(username, UserSessionLoginMethod.Local, session, "حساب غیرفعال است.", cancellationToken);
            throw new UnauthorizedAppException("حساب کاربری شما غیرفعال شده است.");
        }

        var snapshot = await permissionResolver.ResolveAsync(user.Id, cancellationToken)
            ?? throw new UnauthorizedAppException(InvalidCredentials);

        await StartSessionAsync(user.Username, UserSessionLoginMethod.Local, session, snapshot, cancellationToken);
        return ToDto(snapshot);
    }

    public async Task<CurrentUserDto> LoginLdapAsync(
        LoginRequest request,
        SessionContext session,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Username, nameof(request.Username));
        var result = await directory.AuthenticateAsync(request.Username.Trim(), request.Password ?? string.Empty, cancellationToken);
        if (!result.Success || result.User is null)
        {
            await RecordFailureAsync(request.Username.Trim(), UserSessionLoginMethod.Ldap, session, result.Error ?? InvalidCredentials, cancellationToken);
            throw new UnauthorizedAppException(InvalidCredentials);
        }

        var info = result.User;
        var user = await db.Users.FirstOrDefaultAsync(item => item.Username == info.Username, cancellationToken);
        var isNew = user is null;
        if (user is null)
        {
            var initialParts = SplitDisplayName(info.DisplayName, info.Username);
            user = new ApplicationUser(info.Username, initialParts.First, initialParts.Last, isDirectoryUser: true);
        }
        else if (!user.IsDirectoryUser)
        {
            await RecordFailureAsync(info.Username, UserSessionLoginMethod.Ldap, session, "نام کاربری با حساب محلی یکسان است.", cancellationToken);
            throw new UnauthorizedAppException(InvalidCredentials);
        }

        if (user.IsDisabled)
        {
            await RecordFailureAsync(info.Username, UserSessionLoginMethod.Ldap, session, "حساب غیرفعال است.", cancellationToken);
            throw new UnauthorizedAppException("حساب کاربری شما غیرفعال شده است.");
        }

        var groupsJson = JsonSerializer.Serialize(info.Groups);
        var parts = SplitDisplayName(info.DisplayName, info.Username);
        user.UpdateProfile(parts.First, parts.Last, info.MobilePhone, info.Email, user.OrganizationalUnit);
        user.SyncDirectory(
            parts.First,
            parts.Last,
            user.OrganizationalUnit,
            groupsJson,
            info.DistinguishedName ?? $"CN={info.DisplayName}");

        if (isNew)
        {
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(cancellationToken);

        var snapshot = await permissionResolver.ResolveAsync(user.Id, cancellationToken);
        if (snapshot is null || (!snapshot.IsLocalAdmin && snapshot.Permissions.Grants.Count == 0))
        {
            await RecordFailureAsync(info.Username, UserSessionLoginMethod.Ldap, session, "حساب در گروه‌های مجاز نیست.", cancellationToken);
            throw new ForbiddenAppException("حساب شما در گروه‌های مجاز سامانه نیست.");
        }

        await StartSessionAsync(user.Username, UserSessionLoginMethod.Ldap, session, snapshot, cancellationToken);
        return ToDto(snapshot);
    }

    public async Task<CurrentUserDto> LoginWindowsAsync(
        string username,
        SessionContext session,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username, nameof(username));
        var normalized = username.Contains('\\') ? username[(username.LastIndexOf('\\') + 1)..] : username;
        var user = await db.Users.FirstOrDefaultAsync(item => item.Username == normalized, cancellationToken);
        var isNew = user is null;
        var info = await directory.FindByUsernameAsync(normalized, cancellationToken);

        if (user is null)
        {
            var display = info?.DisplayName ?? normalized;
            var parts = SplitDisplayName(display, normalized);
            user = new ApplicationUser(normalized, parts.First, parts.Last, isDirectoryUser: true);
            db.Users.Add(user);
        }

        if (user.IsDisabled)
        {
            await RecordFailureAsync(normalized, UserSessionLoginMethod.Windows, session, "حساب غیرفعال است.", cancellationToken);
            throw new UnauthorizedAppException("حساب کاربری شما غیرفعال شده است.");
        }

        if (isNew && info is null)
        {
            db.Users.Remove(user);
            await RecordFailureAsync(normalized, UserSessionLoginMethod.Windows, session, "حساب در دایرکتوری پیدا نشد.", cancellationToken);
            throw new ForbiddenAppException("حساب شما در دایرکتوری پیدا نشد.");
        }

        if (info is not null)
        {
            user.SyncDirectory(
                SplitDisplayName(info.DisplayName, normalized).First,
                SplitDisplayName(info.DisplayName, normalized).Last,
                user.OrganizationalUnit,
                JsonSerializer.Serialize(info.Groups),
                info.DistinguishedName ?? $"CN={info.DisplayName}");
        }

        await db.SaveChangesAsync(cancellationToken);

        var snapshot = await permissionResolver.ResolveAsync(user.Id, cancellationToken);
        if (snapshot is null || (!snapshot.IsLocalAdmin && snapshot.Permissions.Grants.Count == 0))
        {
            await RecordFailureAsync(normalized, UserSessionLoginMethod.Windows, session, "حساب در گروه‌های مجاز نیست.", cancellationToken);
            throw new ForbiddenAppException("حساب شما در گروه‌های مجاز سامانه نیست.");
        }

        await StartSessionAsync(user.Username, UserSessionLoginMethod.Windows, session, snapshot, cancellationToken);
        return ToDto(snapshot);
    }

    public async Task LogoutAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sessionKey, out var key))
        {
            return;
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(item => item.SessionKey == key, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.End(clock.UtcNow, "خروج کاربر.");
        db.AuditEntries.Add(new AuditEntry(session.Username, "Logout", "Session", session.Id.ToString(), "Success", clientIpAddress: session.ClientIpAddress));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CurrentUserDto?> GetCurrentAsync(string username, CancellationToken cancellationToken = default)
    {
        var snapshot = await permissionResolver.ResolveAsync(username, cancellationToken);
        return snapshot is null || snapshot.IsDisabled ? null : ToDto(snapshot);
    }

    public async Task ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Username == username, cancellationToken)
            ?? throw new NotFoundAppException("کاربر پیدا نشد.");

        if (user.IsDirectoryUser)
        {
            throw new ValidationAppException("رمز کاربران دامنه فقط در Active Directory تغییر می‌کند.");
        }

        if (string.IsNullOrEmpty(user.PasswordHash) || !passwordHasher.Verify(user.PasswordHash, currentPassword))
        {
            throw new UnauthorizedAppException("رمز فعلی معتبر نیست.");
        }

        if (newPassword.Length < 8)
        {
            throw new ValidationAppException("رمز جدید باید حداقل ۸ نویسه باشد.");
        }

        user.SetPasswordHash(passwordHasher.Hash(newPassword));
        db.AuditEntries.Add(new AuditEntry(username, "ChangePassword", "User", user.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task StartSessionAsync(
        string username,
        UserSessionLoginMethod method,
        SessionContext context,
        UserAccessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.SessionKey, out var key))
        {
            key = Guid.NewGuid();
        }

        var session = new UserSession(username, method, context.ClientIpAddress, key);
        db.UserSessions.Add(session);
        db.AuditEntries.Add(new AuditEntry(username, "Login", "Session", session.Id.ToString(), "Success", afterJson: JsonSerializer.Serialize(new { method = method.ToString(), roles = snapshot.Roles }), clientIpAddress: context.ClientIpAddress));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        string username,
        UserSessionLoginMethod method,
        SessionContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        db.UserSessions.Add(UserSession.Failed(username, method, context.ClientIpAddress, reason));
        db.AuditEntries.Add(new AuditEntry(username, "Login", "Session", null, "Failed", reason, clientIpAddress: context.ClientIpAddress));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static CurrentUserDto ToDto(UserAccessSnapshot snapshot) => new(
        snapshot.Username,
        snapshot.DisplayName,
        snapshot.OrganizationalUnit,
        snapshot.IsLocalAdmin,
        snapshot.Roles,
        snapshot.EffectiveCodes);

    private static (string First, string Last) SplitDisplayName(string? displayName, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName.Trim();
        var space = value.IndexOf(' ');
        return space <= 0
            ? (value, "—")
            : (value[..space], value[(space + 1)..].Trim());
    }
}
