using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class RecommendationScheduleEndpointTests(TravelOsApiFactory factory)
    : IClassFixture<TravelOsApiFactory>
{
    private HttpClient? _client;
    private HttpClient Client => _client ??= factory.CreateClient();

    [PostgresFact]
    public async Task Schedule_RequiresAuthentication()
    {
        var seeded = await SeedTripAsync(addItinerary: true, addMember: false);
        using var response = await Client.PostAsJsonAsync(
            ScheduleUrl(seeded.TripId), ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task Schedule_RejectsTripMemberBecauseOnlyOwnerMaySchedule()
    {
        var seeded = await SeedTripAsync(addItinerary: true, addMember: true);
        using var request = AuthorizedRequest(seeded.TripId, seeded.MemberId);
        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [PostgresFact]
    public async Task Schedule_ReturnsSlotsForOwnerAndDoesNotPersistAnActivity()
    {
        var seeded = await SeedTripAsync(addItinerary: true, addMember: false);
        using var request = AuthorizedRequest(seeded.TripId, seeded.OwnerId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var slots = body.RootElement.GetProperty("slots");
        Assert.NotEmpty(slots.EnumerateArray());
        Assert.Equal("09:00", slots[0].GetProperty("startTime").GetString());
        Assert.Equal("09:45", slots[0].GetProperty("endTime").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        var activities = await db.Activities.CountAsync(x => x.ItineraryDayId == seeded.DayId);
        Assert.Equal(1, activities);
    }

    [PostgresFact]
    public async Task Schedule_RejectsApprovedItinerary()
    {
        var seeded = await SeedTripAsync(addItinerary: true, addMember: false, approved: true);
        using var request = AuthorizedRequest(seeded.TripId, seeded.OwnerId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [PostgresFact]
    public async Task Schedule_RejectsTripWithoutAnItinerary()
    {
        var seeded = await SeedTripAsync(addItinerary: false, addMember: false);
        using var request = AuthorizedRequest(seeded.TripId, seeded.OwnerId);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [PostgresFact]
    public async Task Schedule_RejectsInvalidDuration()
    {
        var seeded = await SeedTripAsync(addItinerary: true, addMember: false);
        using var request = AuthorizedRequest(seeded.TripId, seeded.OwnerId, durationMinutes: 10);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string ScheduleUrl(Guid tripId) =>
        $"/api/v1/trips/{tripId}/itinerary/recommendations/schedule";

    private HttpRequestMessage AuthorizedRequest(Guid tripId, Guid userId, int durationMinutes = 45)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ScheduleUrl(tripId))
        {
            Content = JsonContent.Create(ValidRequest(durationMinutes))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId));
        return request;
    }

    private async Task<SeededTrip> SeedTripAsync(bool addItinerary, bool addMember, bool approved = false)
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var dayId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        db.Users.AddRange(
            NewUser(ownerId, $"owner-{ownerId:N}@example.test"),
            NewUser(memberId, $"member-{memberId:N}@example.test"));

        var trip = new Trip
        {
            Id = tripId,
            OwnerUserId = ownerId,
            Name = "Integration test trip",
            Destination = "Colombo",
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 3),
            CurrencyCode = "LKR",
            Members = new List<TripMember>
            {
                new() { TripId = tripId, UserId = ownerId, Role = "owner" }
            }
        };
        if (addMember)
            trip.Members.Add(new TripMember { TripId = tripId, UserId = memberId, Role = "member" });

        db.Trips.Add(trip);
        if (addItinerary)
        {
            db.Itineraries.Add(new Itinerary
            {
                TripId = tripId,
                Version = 1,
                ApprovedByUserId = approved ? ownerId : null,
                ApprovedAt = approved ? DateTimeOffset.UtcNow : null,
                Days = new List<ItineraryDay>
                {
                    new()
                    {
                        Id = dayId,
                        DayNumber = 1,
                        Date = new DateOnly(2026, 12, 1),
                        Activities = new List<Activity>
                        {
                            new()
                            {
                                Title = "Morning museum",
                                StartTime = new TimeOnly(10, 0),
                                EndTime = new TimeOnly(11, 0),
                                DurationMinutes = 60,
                                SortOrder = 1
                            }
                        }
                    }
                }
            });
        }

        await db.SaveChangesAsync();
        return new SeededTrip(ownerId, memberId, tripId, dayId);
    }

    private static User NewUser(Guid id, string email) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        DisplayName = "Integration Test User",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static object ValidRequest(int durationMinutes = 45) => new
    {
        dayNumber = 1,
        title = "Botanical garden",
        durationMinutes,
        dayStartTime = "09:00",
        dayEndTime = "17:00",
        bufferMinutes = 15
    };

    private static string CreateToken(Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TravelOsApiFactory.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TravelOsApiFactory.TestIssuer,
            audience: TravelOsApiFactory.TestAudience,
            claims: [new Claim("sub", userId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record SeededTrip(Guid OwnerId, Guid MemberId, Guid TripId, Guid DayId);
}
