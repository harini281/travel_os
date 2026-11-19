using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TravelOS.PlatformApi.Tests;

public sealed class TravelOsApiFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "TravelOS-Integration-Test-Signing-Key-2026-Only";
    public const string TestIssuer = "TravelOS.IntegrationTests";
    public const string TestAudience = "TravelOS.IntegrationTests.Client";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRAVELOS_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "TRAVELOS_TEST_CONNECTION_STRING must point to a disposable PostgreSQL integration-test database.");

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TravelOS"] = connectionString,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:SigningKey"] = TestSigningKey
            });
        });
    }
}

public sealed class PostgresFactAttribute : Xunit.FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRAVELOS_TEST_CONNECTION_STRING")))
            Skip = "Set TRAVELOS_TEST_CONNECTION_STRING to run PostgreSQL-backed API integration tests.";
    }
}
