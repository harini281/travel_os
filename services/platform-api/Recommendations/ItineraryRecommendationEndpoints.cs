using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Security;
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
        group.MapPost("/recommendations/schedule", SuggestScheduleAsync)
            .WithName("SuggestRecommendationSchedule");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }


    private static async Task<IResult> SuggestScheduleAsync(
        Guid tripId,
        RecommendationScheduleRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();

        var trip = await db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .Include(x => x.Days).ThenInclude(x => x.Activities)
            .FirstOrDefaultAsync(ct);
        if (itinerary is null) return Results.Conflict(new { message = "Create an itinerary before scheduling recommendations." });
        if (itinerary.ApprovedAt is not null) return Results.Conflict(new { message = "Approved itineraries are read-only." });

        var day = itinerary.Days.FirstOrDefault(x => x.DayNumber == request.DayNumber);
        if (day is null) return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["dayNumber"] = ["Choose a day in the current itinerary."]
        });

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 180 ||
            request.DurationMinutes is < 15 or > 720 ||
            request.BufferMinutes is < 0 or > 240 ||
            !TimeOnly.TryParse(request.DayStartTime, out var dayStart) ||
            !TimeOnly.TryParse(request.DayEndTime, out var dayEnd) ||
            dayEnd <= dayStart)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["schedule"] = ["Provide a recommendation title, a duration from 15 to 720 minutes, a buffer from 0 to 240 minutes, and a valid planning window where the end is after the start."]
            });
        }

        var schedule = RecommendationSchedulePlanner.Build(
            day.Activities.Select(activity => new RecommendationScheduleActivity(
                activity.Title, activity.StartTime, activity.EndTime)),
            dayStart,
            dayEnd,
            request.DurationMinutes,
            request.BufferMinutes);

        return Results.Ok(new
        {
            tripId,
            dayNumber = day.DayNumber,
            activityDate = day.Date,
            title = request.Title.Trim(),
            durationMinutes = request.DurationMinutes,
            planningWindow = new { startTime = dayStart.ToString("HH:mm"), endTime = dayEnd.ToString("HH:mm") },
            bufferMinutes = request.BufferMinutes,
            slots = schedule.Slots.Take(5).ToArray(),
            conflicts = schedule.Conflicts,
            warnings = schedule.Warnings,
            disclaimer = "Slots are calculated from saved activity times and the planning window you supplied. Travel time, venue opening hours, availability and live conditions are not verified. Nothing is saved until you explicitly choose a slot."
        });
    }

    private sealed record RecommendationScheduleRequest(
        int DayNumber,
        string Title,
        int DurationMinutes,
        string DayStartTime,
        string DayEndTime,
        int BufferMinutes = 30);

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

        if (request.DayNumber < 1 || request.TravellerCount is < 1 or > 30 ||
            request.Focus is not ("all" or "weather" or "budget" or "group" or "schedule") ||
            request.Question?.Length > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Choose a valid itinerary day, focus, traveller count and question."]
            });

        var day = itinerary.Days.FirstOrDefault(x => x.DayNumber == request.DayNumber);
        if (day is null) return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["activityDate"] = ["Choose a date that belongs to the current itinerary."]
        });

        var travellerCount = request.TravellerCount;

        var preference = await db.TripMemberPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        var groupPreferences = await db.TripMemberPreferences.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .Select(x => x.InterestsJson)
            .ToListAsync(ct);
        var interests = groupPreferences
            .SelectMany(json => System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var existingActivities = day.Activities.OrderBy(x => x.SortOrder)
            .Select(x => x.Title).Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(12).ToArray();

        var activityIds = day.Activities.Select(activity => activity.Id).ToArray();
        var weatherRows = await db.TripWeatherAssessments.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ActivityDate == day.Date &&
                activityIds.Contains(x.ActivityId))
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
                question = string.IsNullOrWhiteSpace(request.Question)
                    ? "Recommend suitable activities for this day."
                    : request.Question,
                focus = request.Focus,
                existingActivities,
                weatherContext,
                planningContext = new[]
                {
                    $"Trip dates: {trip.StartDate:yyyy-MM-dd} to {trip.EndDate:yyyy-MM-dd}.",
                    $"Day {day.DayNumber} currently contains {day.Activities.Count} saved activities.",
                    $"Recommendation focus: {request.Focus}.",
                    $"Traveller travel style: {preference?.TravelStyle ?? "mixed"}."
                }
            };

            using var serviceRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/planner/recommendations")
            {
                Content = JsonContent.Create(aiRequest)
            };
            serviceRequest.AddAiServiceAuthentication(configuration);
            using var response = await client.SendAsync(serviceRequest, ct);

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
                provider = generated.Provider,
                model = generated.Model,
                generatedAtUtc = DateTimeOffset.UtcNow,
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
        catch (JsonException)
        {
            return Results.Problem("The AI service returned an unreadable recommendation response.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private sealed record RecommendationRequest(
        int DayNumber,
        string Focus,
        int TravellerCount = 1,
        string? Question = null);
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
