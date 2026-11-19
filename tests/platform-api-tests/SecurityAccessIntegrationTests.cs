using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class JwtSecurityIntegrationTests(TravelOsApiFactory factory)
    : IClassFixture<TravelOsApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [PostgresFact]
    public async Task ProtectedTripEndpoint_RejectsMissingAndMalformedTokens()
    {
        var path = $"/api/v1/trips/{Guid.NewGuid()}";

        using var missing = await Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var malformedRequest = new HttpRequestMessage(HttpMethod.Get, path);
        malformedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        using var malformed = await Client.SendAsync(malformedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
    }

    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("not-before")]
    [PostgresTheory]
    public async Task ProtectedTripEndpoint_RejectsInvalidJwtValidationCases(string variant)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/trips/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(variant));

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task ProtectedTripEndpoint_AcceptsCorrectSignatureIssuerAudienceAndLifetime()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/trips/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("valid"));

        using var response = await Client.SendAsync(request);

        // The token is valid; the randomly selected trip does not exist.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string CreateToken(string variant)
    {
        var now = DateTime.UtcNow;
        var signingKey = variant == "wrong-key"
            ? new string('x', 96)
            : TravelOsApiFactory.TestSigningKey;
        var issuer = variant == "wrong-issuer" ? "TravelOS.WrongIssuer" : TravelOsApiFactory.TestIssuer;
        var audience = variant == "wrong-audience" ? "TravelOS.WrongAudience" : TravelOsApiFactory.TestAudience;
        var notBefore = variant == "not-before" ? now.AddHours(1) : now.AddMinutes(-1);
        var expires = variant == "expired" ? now.AddHours(-1) : now.AddHours(2);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class TripAccessSecurityIntegrationTests(TravelOsApiFactory factory)
    : IClassFixture<TravelOsApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [PostgresFact]
    public async Task TripAccess_RestrictsReadToOwnerAndCurrentMembers_AndUpdateToOwner()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
            db.Users.AddRange(
                NewUser(ownerId), NewUser(memberId), NewUser(outsiderId));
            db.Trips.Add(new Trip
            {
                Id = tripId,
                OwnerUserId = ownerId,
                Name = "Security test trip",
                Destination = "Colombo",
                StartDate = new DateOnly(2026, 12, 1),
                EndDate = new DateOnly(2026, 12, 3),
                CurrencyCode = "LKR",
                Members =
                [
                    new TripMember { TripId = tripId, UserId = ownerId, Role = "owner" },
                    new TripMember { TripId = tripId, UserId = memberId, Role = "member" }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var ownerRead = AuthorizedRequest(ownerId, HttpMethod.Get, $"/api/v1/trips/{tripId}");
        using var ownerResponse = await Client.SendAsync(ownerRead);
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        using var memberRead = AuthorizedRequest(memberId, HttpMethod.Get, $"/api/v1/trips/{tripId}");
        using var memberResponse = await Client.SendAsync(memberRead);
        Assert.Equal(HttpStatusCode.OK, memberResponse.StatusCode);

        using var outsiderRead = AuthorizedRequest(outsiderId, HttpMethod.Get, $"/api/v1/trips/{tripId}");
        using var outsiderResponse = await Client.SendAsync(outsiderRead);
        Assert.Equal(HttpStatusCode.NotFound, outsiderResponse.StatusCode);

        var memberScopedRoutes = new[]
        {
            $"/api/v1/trips/{tripId}/itinerary",
            $"/api/v1/trips/{tripId}/expenses",
            $"/api/v1/trips/{tripId}/receipts",
            $"/api/v1/trips/{tripId}/budget",
            $"/api/v1/trips/{tripId}/settlements",
            $"/api/v1/trips/{tripId}/flights",
            $"/api/v1/trips/{tripId}/weather-disruptions",
            $"/api/v1/trips/{tripId}/budget-intelligence",
            $"/api/v1/trips/{tripId}/expense-intelligence",
            $"/api/v1/trips/{tripId}/assistant/briefing"
        };

        foreach (var route in memberScopedRoutes)
        {
            using var memberRequest = AuthorizedRequest(memberId, HttpMethod.Get, route);
            using var memberResponse = await Client.SendAsync(memberRequest);
            Assert.True(
                memberResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
                $"Current member was unexpectedly blocked from {route}: {(int)memberResponse.StatusCode}");

            using var outsiderRequest = AuthorizedRequest(outsiderId, HttpMethod.Get, route);
            using var outsiderResourceResponse = await Client.SendAsync(outsiderRequest);
            Assert.Equal(HttpStatusCode.NotFound, outsiderResourceResponse.StatusCode);
        }

        var updateBody = new
        {
            name = "Unauthorized member edit",
            destination = "Colombo",
            startDate = "2026-12-01",
            endDate = "2026-12-03",
            currencyCode = "LKR"
        };
        using var memberUpdate = AuthorizedRequest(
            memberId, HttpMethod.Put, $"/api/v1/trips/{tripId}", updateBody);
        using var memberUpdateResponse = await Client.SendAsync(memberUpdate);
        Assert.Equal(HttpStatusCode.Forbidden, memberUpdateResponse.StatusCode);

        using var outsiderUpdate = AuthorizedRequest(
            outsiderId, HttpMethod.Put, $"/api/v1/trips/{tripId}", updateBody);
        using var outsiderUpdateResponse = await Client.SendAsync(outsiderUpdate);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderUpdateResponse.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        Assert.Equal("Security test trip", await verifyDb.Trips
            .Where(x => x.Id == tripId).Select(x => x.Name).SingleAsync());
    }

    private static HttpRequestMessage AuthorizedRequest(
        Guid userId, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId));
        return request;
    }

    private static string CreateToken(Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TravelOsApiFactory.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TravelOsApiFactory.TestIssuer,
            audience: TravelOsApiFactory.TestAudience,
            claims: [new Claim("sub", userId.ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static User NewUser(Guid id)
    {
        var email = $"security-{id:N}@example.test";
        return new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = "Security test user",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
