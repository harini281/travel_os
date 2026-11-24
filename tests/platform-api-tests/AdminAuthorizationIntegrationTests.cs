using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class AdminAuthorizationIntegrationTests(TravelOsApiFactory factory)
    : IClassFixture<TravelOsApiFactory>
{
    [Fact]
    public async Task AdminEndpoints_RejectAnonymousRequests()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/api/v1/admin/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_RejectAuthenticatedTravellers()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/admin/dashboard");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken("Traveller"));

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string CreateToken(string role)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(TravelOsApiFactory.TestSigningKey));
        var credentials = new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TravelOsApiFactory.TestIssuer,
            audience: TravelOsApiFactory.TestAudience,
            claims:
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
