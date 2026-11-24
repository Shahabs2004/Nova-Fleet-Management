using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace NovaFleet.Infrastructure.Persistence;

/// <summary>قفل مشترک منابع تخصیص؛ همیشه راننده پیش از خودرو و فقط داخل تراکنش سریالایز گرفته می‌شود.</summary>
internal static class ResourceLocks
{
    public static async Task LockAsync(
        NovaFleetDbContext db,
        Guid driverId,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await LockDriverAsync(db, driverId, cancellationToken);
        await LockVehicleAsync(db, vehicleId, cancellationToken);
    }

    public static Task LockDriverAsync(
        NovaFleetDbContext db,
        Guid driverId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer()) return Task.CompletedTask;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [Drivers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {driverId}",
            cancellationToken);
    }

    public static Task LockVehicleAsync(
        NovaFleetDbContext db,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer()) return Task.CompletedTask;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [Vehicles] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {vehicleId}",
            cancellationToken);
    }

    public static bool IsSqlContention(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 1222 }) return true;
        }

        return false;
    }
}
