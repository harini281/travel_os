using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Itineraries;

public static class ItineraryRecommendationEndpoints
{
    public static IEndpointRouteBuilder MapItineraryRecommendationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/trips/{tripId:guid}/itinerary/recommendations",
            RecommendAsync)
            .WithTags("Itinerary recommendations")
            .RequireAuthorization()
            .RequireRateLimiting("ai-generation")
            .WithName("RecommendTripActivities");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<IResult> RecommendAsync(
        Guid tripId,
        ItineraryRecommendationRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        IHttpClientFactory clients,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();

        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == tripId && (x.OwnerUserId == userId ||
                db.TripMembers.Any(member => member.TripId == x.Id && member.UserId == userId)), ct);
        if (trip is null) return Results.NotFound();

        if (request.DayNumber is < 1 or > 31)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["dayNumber"] = ["Choose a valid itinerary day number."] });
        if (request.Question?.Trim().Length > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["question"] = ["Question must be 500 characters or fewer."] });

        var itinerary = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Id, x.Days })
            .FirstOrDefaultAsync(ct);
        if (itinerary is null) return Results.Conflict(new { message = "Create an itinerary before requesting activity recommendations." });

        var day = await db.ItineraryDays.AsNoTracking()
            .Where(x => x.ItineraryId == itinerary.Id && x.DayNumber == request.DayNumber)
            .Select(x => new { x.Date, Activities = x.Activities.OrderBy(a => a.SortOrder)
                .Select(a => a.Title).ToList() })
            .FirstOrDefaultAsync(ct);
        if (day is null) return Results.NotFound();

        var interests = await db.TripMemberPreferences.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .Select(x => x.InterestsJson)
            .ToListAsync(ct);
        var allInterests = interests.SelectMany(json =>
        {
            try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
            catch (JsonException) { return []; }
        }).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();

        var pace = await db.TripMemberPreferences.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .GroupBy(x => x.Pace)
            .Select(group => new { Pace = group.Key, Count = group.Count() })
            .OrderByDescending(x => x.Count)
            .Select(x => x.Pace)
            .FirstOrDefaultAsync(ct) ?? "balanced";

        var weather = await db.TripWeatherAssessments.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ActivityDate == day.Date)
            .OrderByDescending(x => x.ForecastFetchedAt)
            .Select(x => new { x.ActivityId, x.ActivityTitle, x.Severity, x.Summary, x.Recommendation, x.ForecastFetchedAt })
            .ToListAsync(ct);
        var latestWeather = weather.GroupBy(x => x.ActivityId).Select(group => group.First())
            .Select(x => $"{x.ActivityTitle}: {x.Severity}. {x.Summary} {x.Recommendation}".Trim())
            .Take(8).ToArray();

        var budget = await db.Budgets.AsNoTracking().FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        decimal? remainingBudget = null;
        if (budget is not null)
        {
            var spent = await db.TripExpenses.AsNoTracking()
                .Where(x => x.TripId == tripId && x.CurrencyCode == budget.CurrencyCode)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            remainingBudget = Math.Max(0m, budget.Amount - spent);
        }

        var planningContext = new List<string>();
        if (request.Focus is "weather" or "all" && latestWeather.Length == 0)
            planningContext.Add("No saved weather assessment exists for this day. Do not assume current weather.");
        if (request.Focus is "budget" or "all" && remainingBudget.HasValue)
            planningContext.Add($"Recorded remaining trip budget estimate: {remainingBudget.Value:0.##} {budget!.CurrencyCode}. This is not a daily allowance.");
        if (day.Activities.Count == 0)
            planningContext.Add("This day has no saved activities yet.");
        if (day.Activities.Count > 0)
            planningContext.Add($"Current activities: {string.Join("; ", day.Activities)}.");

        var baseUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:8001";
        var client = clients.CreateClient("ai-planner");
        try
        {
            client.BaseAddress = new Uri(baseUrl);
            var aiRequest = new
            {
                destination = trip.Destination,
                activityDate = day.Date,
                currency = trip.CurrencyCode,
                travellerCount = request.TravellerCount,
                interests = allInterests,
                travelPace = pace is "relaxed" or "balanced" or "fast" ? pace : "balanced",
                dailyBudgetRemaining = remainingBudget,
                question = request.Question?.Trim() is { Length: > 0 } question
                    ? question : $"Suggest {request.Focus} activities for this day.",
                existingActivities = day.Activities,
                weatherContext = request.Focus is "weather" or "all" ? latestWeather : [],
                planningContext
            };

            using var response = await client.PostAsJsonAsync(
                "/api/v1/planner/recommendations", aiRequest, cancellationToken: ct);
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                return Results.Problem("The AI recommendation provider is not configured. No suggestions were generated.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            if (!response.IsSuccessStatusCode)
                return Results.Problem("The AI service could not generate activity recommendations.",
                    statusCode: StatusCodes.Status502BadGateway);

            var generated = await response.Content.ReadFromJsonAsync<AiRecommendationResponse>(cancellationToken: ct);
            if (generated is null || generated.Status != "generated" ||
                generated.Recommendations.Count is < 1 or > 6)
                return Results.Problem("The AI service returned no usable recommendations. No itinerary changes were made.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            return Results.Ok(new ItineraryRecommendationsResponse(
                tripId, request.DayNumber, day.Date, generated.Provider, generated.Model,
                generated.Summary, generated.Recommendations, generated.Warnings,
                DateTimeOffset.UtcNow,
                "Recommendations are unverified ideas. Check venue details, accessibility, opening hours and prices. No itinerary changes have been made."));
        }
        catch (UriFormatException)
        {
            return Results.Problem("The AI service base URL is invalid.", statusCode: 500);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The AI service is unavailable. No recommendations were generated.", statusCode: 503);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem("The AI service timed out. Please try again.", statusCode: 504);
        }
        catch (JsonException)
        {
            return Results.Problem("The AI service returned an unreadable response.", statusCode: 502);
        }
    }
}

public sealed record ItineraryRecommendationRequest(
    int DayNumber,
    string Focus = "all",
    int TravellerCount = 1,
    string? Question = null);

public sealed record AiActivityRecommendation(
    string Title,
    string Category,
    string Reason,
    int? SuggestedDurationMinutes,
    decimal? EstimatedCost,
    string? CurrencyCode,
    string BestFor,
    string VerificationNote);

public sealed record AiRecommendationResponse(
    string Status,
    string Provider,
    string? Model,
    IReadOnlyList<AiActivityRecommendation> Recommendations,
    string Summary,
    IReadOnlyList<string> Warnings);

public sealed record ItineraryRecommendationsResponse(
    Guid TripId,
    int DayNumber,
    DateOnly ActivityDate,
    string Provider,
    string? Model,
    string Summary,
    IReadOnlyList<AiActivityRecommendation> Recommendations,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc,
    string Disclaimer);
