namespace NovaFleet.Application.Abstractions;

/// <summary>اطلاعات محیط درخواست که برای ممیزی و محدودیت نرخ لازم است.</summary>
public interface IRequestContext
{
    string? ClientIpAddress { get; }

    string? CorrelationId { get; }
}

public sealed class EmptyRequestContext : IRequestContext
{
    public static EmptyRequestContext Instance { get; } = new();

    public string? ClientIpAddress => null;

    public string? CorrelationId => null;
}
