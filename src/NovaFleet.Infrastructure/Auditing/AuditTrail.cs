using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovaFleet.Application.Abstractions;
using NovaFleet.Domain.Auditing;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Auditing;

/// <summary>ثبت ممیزی با هویت کاربر جاری و اطلاعات درخواست؛ پیام خطای ثبت به عملیات اصلی برنمی‌گردد.</summary>
public sealed class AuditTrail(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    ILogger<AuditTrail> logger) : IAuditTrail
{
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            var actor = currentUser.IsAuthenticated ? currentUser.Username : "system";
            var entry = new AuditEntry(
                actor,
                record.Action,
                record.EntityType,
                record.EntityId,
                record.Outcome,
                record.Reason,
                Serialize(record.Before),
                Serialize(record.After),
                requestContext.ClientIpAddress,
                requestContext.CorrelationId);

            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ثبت رکورد ممیزی برای {Action} روی {EntityType} ناموفق بود.", record.Action, record.EntityType);
        }
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value);
}
