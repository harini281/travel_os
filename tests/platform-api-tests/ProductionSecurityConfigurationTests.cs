using Microsoft.Extensions.Configuration;
using TravelOS.PlatformApi.Auth;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class ProductionSecurityConfigurationTests
{
    [Fact]
    public void Production_requires_a_database_connection_string()
    {
        var configuration = BuildConfiguration();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecurityConfiguration.ValidateDatabaseConnectionString(configuration, production: true));

        Assert.Contains("ConnectionStrings:TravelOS", exception.Message);
    }

    [Fact]
    public void Production_rejects_malformed_database_connection_strings()
    {
        var configuration = BuildConfiguration(("ConnectionStrings:TravelOS", "not-a-postgres-connection-string"));

        Assert.Throws<InvalidOperationException>(
            () => ProductionSecurityConfiguration.ValidateDatabaseConnectionString(configuration, production: true));
    }

    [Theory]
    [InlineData("Host=postgres;Database=travelos;Username=travelos;Password=secret")]
    [InlineData("Host=db.example.test;Port=5432;Database=travelos;Username=app;Password=secret")]
    public void Production_accepts_complete_postgres_connection_strings(string connectionString)
    {
        var configuration = BuildConfiguration(("ConnectionStrings:TravelOS", connectionString));

        ProductionSecurityConfiguration.ValidateDatabaseConnectionString(configuration, production: true);
    }

    [Fact]
    public void Development_does_not_require_a_database_connection_string_at_startup()
    {
        ProductionSecurityConfiguration.ValidateDatabaseConnectionString(
            BuildConfiguration(), production: false);
    }

    [Fact]
    public void Production_requires_a_configured_https_origin()
    {
        var configuration = BuildConfiguration(("Jwt:Issuer", "travelos-prod"), ("Jwt:Audience", "travelos-web"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecurityConfiguration.GetAllowedOrigins(configuration, production: true));

        Assert.Contains("HTTPS", exception.Message);
    }

    [Theory]
    [InlineData("http://travelos.example.com")]
    [InlineData("*")]
    [InlineData("https://travelos.example.com/admin")]
    [InlineData("https://user:password@travelos.example.com")]
    public void Production_rejects_unsafe_cors_origins(string origin)
    {
        var configuration = BuildConfiguration(
            ("Cors:AllowedOrigins:0", origin));

        Assert.Throws<InvalidOperationException>(
            () => ProductionSecurityConfiguration.GetAllowedOrigins(configuration, production: true));
    }

    [Fact]
    public void Production_accepts_an_exact_https_origin()
    {
        var configuration = BuildConfiguration(("Cors:AllowedOrigins:0", "https://travelos.example.com"));

        Assert.Equal(
            new[] { "https://travelos.example.com" },
            ProductionSecurityConfiguration.GetAllowedOrigins(configuration, production: true));
    }

    [Fact]
    public void Development_keeps_the_localhost_origin_default()
    {
        var configuration = BuildConfiguration();

        Assert.Equal(
            new[] { "http://localhost:5173" },
            ProductionSecurityConfiguration.GetAllowedOrigins(configuration, production: false));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("TravelOS-CI-Only-Signing-Key-Not-For-Production-2026")]
    [InlineData("REPLACE_WITH_A_UNIQUE_RANDOM_SECRET_AT_LEAST_32_BYTES")]
    public void Production_rejects_weak_or_placeholder_signing_keys(string key)
    {
        var configuration = BuildConfiguration(
            ("Jwt:Issuer", "travelos-prod"),
            ("Jwt:Audience", "travelos-web"),
            ("Jwt:SigningKey", key));

        Assert.Throws<InvalidOperationException>(
            () => ProductionSecurityConfiguration.ValidateJwt(configuration, production: true));
    }

    [Fact]
    public void Production_accepts_a_configured_random_signing_key()
    {
        var configuration = BuildConfiguration(
            ("Jwt:Issuer", "travelos-prod"),
            ("Jwt:Audience", "travelos-web"),
            ("Jwt:SigningKey", "vT9!pQ2#kL7@xM4$zR8^nB5&cF1*wH6?"));

        ProductionSecurityConfiguration.ValidateJwt(configuration, production: true);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();
}
