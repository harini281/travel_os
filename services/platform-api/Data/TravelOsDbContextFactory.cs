using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelOS.PlatformApi.Data;

public sealed class TravelOsDbContextFactory : IDesignTimeDbContextFactory<TravelOsDbContext>
{
    public TravelOsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__TravelOS")
            ?? "Host=localhost;Port=5432;Database=travelos;Username=travelos;Password=change-me-local-only";

        var options = new DbContextOptionsBuilder<TravelOsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TravelOsDbContext(options);
    }
}
