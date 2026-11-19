using System.Net.Http.Headers;

namespace TravelOS.PlatformApi.Security;

public static class AiServiceAuthentication
{
    public static void AddAiServiceAuthentication(this HttpRequestMessage request, IConfiguration configuration)
    {
        var token = configuration["AI_SERVICE_TOKEN"];
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new InvalidOperationException("Internal AI service authentication is not configured with a sufficiently long token.");

        request.Headers.TryAddWithoutValidation("X-TravelOS-Service-Token", token);
    }
}
