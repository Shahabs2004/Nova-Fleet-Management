using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Auditing;

/// <summary>
/// رکورد ممیزی دائمی و غیرقابل‌ویرایش؛ قبل/بعد تغییر برای بررسی نگهداری می‌شود.
/// </summary>
public sealed class AuditEntry : Entity
{
    private AuditEntry()
    {
    }

    public string ActorUsername { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string? EntityId { get; private set; }

    public string Outcome { get; private set; } = "Success";

    public string? Reason { get; private set; }

    public string? BeforeJson { get; private set; }

    public string? AfterJson { get; private set; }

    public string? ClientIpAddress { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public AuditEntry(
        string actorUsername,
        string action,
        string entityType,
        string? entityId,
        string outcome = "Success",
        string? reason = null,
        string? beforeJson = null,
        string? afterJson = null,
        string? clientIpAddress = null,
        string? correlationId = null)
    {
        ActorUsername = string.IsNullOrWhiteSpace(actorUsername) ? "system" : actorUsername.Trim();
        Action = string.IsNullOrWhiteSpace(action) ? "unknown" : action.Trim();
        EntityType = string.IsNullOrWhiteSpace(entityType) ? "unknown" : entityType.Trim();
        EntityId = entityId;
        Outcome = outcome;
        Reason = reason;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        ClientIpAddress = clientIpAddress;
        CorrelationId = correlationId;
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }
}
