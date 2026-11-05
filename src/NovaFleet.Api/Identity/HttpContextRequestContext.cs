using NovaFleet.Application.Abstractions;

namespace NovaFleet.Api.Identity;

/// <summary>اطلاعات محیط درخواست HTTP برای ممیزی؛ نشانی IP و شناسه ردیابی درخواست.</summary>
public sealed class HttpContextRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? ClientIpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
