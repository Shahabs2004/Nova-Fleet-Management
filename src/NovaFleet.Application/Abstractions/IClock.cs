namespace NovaFleet.Application.Abstractions;

/// <summary>ساعت مشترک سامانه؛ در آزمون‌ها قابل جایگزینی است.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
