using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Sessions;

/// <summary>روش ورود کاربر برای سوابق نشست.</summary>
public enum UserSessionLoginMethod
{
    Local = 1,
    Ldap = 2,
    Windows = 3
}

public enum UserSessionStatus
{
    Active = 1,
    Ended = 2,
    Expired = 3,
    Failed = 4
}

/// <summary>سابقه دائمی نشست و ورود کاربر؛ مستقل از گزارش ممیزی.</summary>
public sealed class UserSession : Entity
{
    private UserSession()
    {
    }

    public Guid SessionKey { get; private set; } = Guid.NewGuid();

    public string Username { get; private set; } = string.Empty;

    public UserSessionLoginMethod LoginMethod { get; private set; }

    public UserSessionStatus Status { get; private set; } = UserSessionStatus.Active;

    public DateTimeOffset LoginAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActivityAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public string? ClientIpAddress { get; private set; }

    public string? Reason { get; private set; }

    public UserSession(string username, UserSessionLoginMethod loginMethod, string? clientIpAddress, Guid? sessionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username, nameof(username));
        Username = username.Trim();
        LoginMethod = loginMethod;
        ClientIpAddress = clientIpAddress;
        SessionKey = sessionKey ?? Guid.NewGuid();
    }

    public void Touch(DateTimeOffset nowUtc) => LastActivityAtUtc = nowUtc;

    public void End(DateTimeOffset nowUtc, string? reason = null)
    {
        if (Status != UserSessionStatus.Active)
        {
            return;
        }

        Status = UserSessionStatus.Ended;
        EndedAtUtc = nowUtc;
        Reason = reason;
    }

    public void Expire(DateTimeOffset nowUtc, string reason)
    {
        if (Status != UserSessionStatus.Active)
        {
            return;
        }

        Status = UserSessionStatus.Expired;
        EndedAtUtc = nowUtc;
        Reason = reason;
    }

    public static UserSession Failed(string username, UserSessionLoginMethod method, string? clientIpAddress, string reason)
    {
        var session = new UserSession(username, method, clientIpAddress)
        {
            Status = UserSessionStatus.Failed,
            EndedAtUtc = DateTimeOffset.UtcNow,
            Reason = reason
        };
        return session;
    }
}
