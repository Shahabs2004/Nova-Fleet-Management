namespace NovaFleet.Domain.Common;

/// <summary>
/// پایه مشترک موجودیت‌های دامنه؛ شناسه پایدار و زمان ایجاد.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;
}
