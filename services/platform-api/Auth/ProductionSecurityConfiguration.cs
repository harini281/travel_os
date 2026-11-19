using System.Text;
using Microsoft.Extensions.Configuration;

namespace TravelOS.PlatformApi.Auth;

public static class ProductionSecurityConfiguration
{
    public static void ValidateJwt(IConfiguration configuration, bool production)
    {
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        var signingKey = configuration["Jwt:SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 bytes.");

        if (!production) return;

        if (string.IsNullOrWhiteSpace(issuer) || IsPlaceholder(issuer))
            throw new InvalidOperationException("Production requires a real Jwt:Issuer value.");
        if (string.IsNullOrWhiteSpace(audience) || IsPlaceholder(audience))
            throw new InvalidOperationException("Production requires a real Jwt:Audience value.");
        if (IsPlaceholder(signingKey) || signingKey.Distinct().Count() < 12)
            throw new InvalidOperationException("Production Jwt:SigningKey must be a unique, randomly generated secret, not an example or placeholder.");
    }

    public static string[] GetAllowedOrigins(IConfiguration configuration, bool production)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0)
        {
            if (production)
                throw new InvalidOperationException("Production requires at least one explicit HTTPS Cors:AllowedOrigins entry.");
            return ["http://localhost:5173"];
        }

        foreach (var origin in origins)
        {
            if (origin == "*" || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" ||
                (production && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    production
                        ? "Production CORS origins must be exact HTTPS origins without paths, credentials, queries or wildcards."
                        : "CORS origins must be exact origins without paths, credentials, queries or wildcards.");
            }
        }

        return origins;
    }

    private static bool IsPlaceholder(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("replace") ||
               normalized.Contains("placeholder") ||
               normalized.Contains("change-me") ||
               normalized.Contains("travelos-ci") ||
               normalized.Contains("example");
    }
}
