using System.Security.Claims;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Recommendations;

public static class ItineraryRecommendationEndpoints
{
    public static IEndpointRouteBuilder MapItineraryRecommendationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/itinerary")
            .WithTags("Itinerary recommendations")
            .RequireAuthorization();

        group.MapPost("/recommendations", GenerateAsync)
            .RequireRateLimiting("ai-generation")
            .WithName("GenerateItineraryRecommendations");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<IResult> GenerateAsync(
        Guid tripId,
        RecommendationRequest request,
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

        var itinerary = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .Include(x => x.Days).ThenInclude(x => x.Activities)
            .FirstOrDefaultAsync(ct);
        if (itinerary is null) return Results.Conflict(new
        {
            code = "itinerary_missing",
            message = "Create an itinerary before requesting activity recommendations."
        });

        var day = itinerary.Days.FirstOrDefault(x => x.Date == request.ActivityDate);
        if (day is null) return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["activityDate"] = ["Choose a date that belongs to the current itinerary."]
        });

        var travellerCount = await db.TripMembers.CountAsync(x => x.TripId == tripId, ct);
        travellerCount = Math.Clamp(travellerCount, 1, 30);

        var preference = await db.TripMemberPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        var interests = preference is null
            ? Array.Empty<string>()
            : System.Text.Json.JsonSerializer.Deserialize<string[]>(preference.InterestsJson) ?? [];

        var existingActivities = day.Activities.OrderBy(x => x.SortOrder)
            .Select(x => x.Title).Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(12).ToArray();

        var weatherRows = await db.TripWeatherAssessments.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ActivityDate == day.Date &&
                day.Activities.Select(a => a.Id).Contains(x.ActivityId))
            .OrderByDescending(x => x.ForecastFetchedAt)
            .Take(40).ToListAsync(ct);
        var weatherContext = weatherRows
            .GroupBy(x => x.ActivityId)
            .Select(x => x.First())
            .Take(8)
            .Select(x => $"{x.ActivityTitle}: {x.Severity}. {x.Summary} {x.Recommendation}".Trim())
            .ToArray();

        var budget = await db.Budgets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        decimal? dailyBudgetRemaining = null;
        if (budget is { Amount: > 0 })
        {
            var spent = await db.TripExpenses.AsNoTracking()
                .Where(x => x.TripId == tripId && x.CurrencyCode == budget.CurrencyCode)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var planned = await db.BudgetItems.AsNoTracking()
                .Where(x => x.BudgetId == budget.Id && x.CurrencyCode == budget.CurrencyCode)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            dailyBudgetRemaining = Math.Max(0m, budget.Amount - Math.Max(spent, planned));
        }

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
                travellerCount,
                interests = interests.Take(12).ToArray(),
                travelPace = preference?.Pace is "relaxed" or "fast" ? preference.Pace : "balanced",
                dailyBudgetRemaining,
                question = request.Question,
                existingActivities,
                weatherContext,
                planningContext = new[]
                {
                    $"Trip dates: {trip.StartDate:yyyy-MM-dd} to {trip.EndDate:yyyy-MM-dd}.",
                    $"Day {day.DayNumber} currently contains {day.Activities.Count} saved activities.",
                    $"Traveller travel style: {preference?.TravelStyle ?? "mixed"}."
                }
            };

            using var response = await client.PostAsJsonAsync(
                "/api/v1/planner/recommendations", aiRequest, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 503)
                    return Results.Problem("The AI provider is not configured. No recommendations were generated.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                return Results.Problem("The AI service could not generate recommendations.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var generated = await response.Content.ReadFromJsonAsync<RecommendationResponse>(cancellationToken: ct);
            if (generated is null || generated.Status != "generated" ||
                generated.Recommendations.Count is < 1 or > 6)
                return Results.Problem("The AI service returned no generated recommendations.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            if (generated.Recommendations.Any(x =>
                x.EstimatedCost is < 0 ||
                (x.EstimatedCost.HasValue && !string.Equals(x.CurrencyCode, trip.CurrencyCode, StringComparison.OrdinalIgnoreCase))))
                return Results.Problem("The AI service returned invalid recommendation costs.",
                    statusCode: StatusCodes.Status502BadGateway);

            return Results.Ok(new
            {
                tripId,
                dayNumber = day.DayNumber,
                activityDate = day.Date,
                generated.Provider,
                generated.Model,
                generated.Summary,
                generated.Recommendations,
                generated.Warnings,
                disclaimer = "AI suggestions are not verified venue listings or live prices. Review them before changing your itinerary. No saved activities are modified."
            });
        }
        catch (UriFormatException)
        {
            return Results.Problem("The AI service base URL is invalid.", statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The AI service is unavailable. No recommendations were saved.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem("The AI recommendation request timed out. Please try again.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

    private sealed record RecommendationRequest(string Question = "Recommend suitable activities for this day.");
    private sealed record RecommendationResponse(
        string Status,
        string Provider,
        string? Model,
        string Summary,
        IReadOnlyList<ActivityRecommendation> Recommendations,
        IReadOnlyList<string> Warnings);
    private sealed record ActivityRecommendation(
        string Title,
        string Category,
        string Reason,
        int? SuggestedDurationMinutes,
        decimal? EstimatedCost,
        string? CurrencyCode,
        string BestFor,
        string VerificationNote);
}
