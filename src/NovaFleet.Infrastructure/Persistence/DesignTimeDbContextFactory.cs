using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NovaFleet.Infrastructure.Persistence;

/// <summary>کارخانه زمان طراحی برای دستورات dotnet ef؛ رشته اتصال از متغیر محیطی خوانده می‌شود.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NovaFleetDbContext>
{
    public NovaFleetDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOVAFLEET_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("متغیر محیطی NOVAFLEET_CONNECTION_STRING برای دستورات طراحی پایگاه داده تنظیم نشده است.");
        }

        var options = new DbContextOptionsBuilder<NovaFleetDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new NovaFleetDbContext(options);
    }
}
