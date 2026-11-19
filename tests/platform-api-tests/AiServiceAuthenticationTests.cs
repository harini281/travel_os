using Microsoft.Extensions.Configuration;
using TravelOS.PlatformApi.Security;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class AiServiceAuthenticationTests
{
    [Fact]
    public void AddAiServiceAuthentication_SetsTheConfiguredServerOnlyHeader()
    {
        const string token = "0123456789abcdef0123456789abcdef0123456789abcdef";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_SERVICE_TOKEN"] = token
            })
            .Build();

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://ai-api:8001/api/v1/planner/drafts");

        request.AddAiServiceAuthentication(configuration);

        Assert.True(request.Headers.TryGetValues("X-TravelOS-Service-Token", out var values));
        Assert.Equal(token, Assert.Single(values!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void AddAiServiceAuthentication_FailsClosedForMissingOrShortTokens(string? token)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_SERVICE_TOKEN"] = token
            })
            .Build();

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://ai-api:8001/api/v1/planner/drafts");

        Assert.Throws<InvalidOperationException>(
            () => request.AddAiServiceAuthentication(configuration));
        Assert.False(request.Headers.Contains("X-TravelOS-Service-Token"));
    }
}
