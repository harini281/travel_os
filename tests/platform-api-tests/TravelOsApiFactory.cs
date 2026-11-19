using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TravelOS.PlatformApi.Tests;

public sealed class TravelOsApiFactory : WebApplicationFactory<Program>
{
    public static readonly string TestSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    public const string TestIssuer = "TravelOS.IntegrationTests";
    public const string TestAudience = "TravelOS.IntegrationTests.Client";
    public static readonly string ReceiptStoragePath = Path.Combine(
        Path.GetTempPath(), "travelos-security-receipts-" + Guid.NewGuid().ToString("N"));

    static TravelOsApiFactory()
    {
        // Minimal hosting reads configuration while Program starts, before
        // ConfigureWebHost's in-memory configuration callback is applied.
        // Set isolated test-only environment values before WebApplicationFactory
        // boots the application.
        var connectionString = Environment.GetEnvironmentVariable("TRAVELOS_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
            Environment.SetEnvironmentVariable("ConnectionStrings__TravelOS", connectionString);

        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("Receipts__StoragePath", ReceiptStoragePath);
        Directory.CreateDirectory(ReceiptStoragePath);
    }

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

public sealed class PostgresTheoryAttribute : Xunit.TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRAVELOS_TEST_CONNECTION_STRING")))
            Skip = "Set TRAVELOS_TEST_CONNECTION_STRING to run PostgreSQL-backed API integration tests.";
    }
}
