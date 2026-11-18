using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Itineraries;

public static class RouteAnalysisEndpoints
{
    public static IEndpointRouteBuilder MapRouteAnalysisEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/trips/{tripId:guid}/itinerary/route-analysis", AnalyzeRoutesAsync)
            .RequireAuthorization()
            .RequireRateLimiting("itinerary-routing")
            .WithTags("Itineraries")
            .WithName("AnalyzeItineraryRoutes");
        return endpoints;
    }

    private static async Task<IResult> AnalyzeRoutesAsync(
        Guid tripId, int? bufferMinutes, ClaimsPrincipal principal,
        TravelOsDbContext db, IHttpClientFactory clients, CancellationToken ct)
    {
        var claim = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var userId)) return Results.Unauthorized();

        var isMember = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct)
            || await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!isMember) return Results.NotFound();

        var buffer = bufferMinutes ?? 15;
        if (buffer is < 0 or > 180)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bufferMinutes"] = ["Route safety buffer must be between 0 and 180 minutes."]
            });

        var itinerary = await db.Itineraries.AsNoTracking()
            .Include(x => x.Days).ThenInclude(x => x.Activities)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        if (itinerary is null)
            return Results.Ok(new RouteAnalysisResponse(tripId, "driving", buffer, Array.Empty<RouteDayAnalysis>(),
                "Road route estimates only. Public OSRM routing has no live traffic, venue opening-hour, parking or transit data."));

        var client = clients.CreateClient("osrm");
        var dayResults = new List<RouteDayAnalysis>();

        foreach (var day in itinerary.Days.OrderBy(x => x.DayNumber))
        {
            var timed = day.Activities
                .Where(x => x.StartTime.HasValue && x.EndTime.HasValue)
                .OrderBy(x => x.StartTime).ThenBy(x => x.EndTime).ThenBy(x => x.Id)
                .ToArray();

            if (timed.Length < 2)
            {
                dayResults.Add(new RouteDayAnalysis(day.Id, day.DayNumber, day.Date.ToString("yyyy-MM-dd"),
                    Array.Empty<ActivityRouteLeg>(), "At least two timed activities are required."));
                continue;
            }

            if (timed.Length > 25)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [$"day{day.DayNumber}"] = ["Route analysis supports up to 25 timed activities per day."]
                });

            var located = timed
                .Select((activity, index) => new { Activity = activity, ActivityIndex = index })
                .Where(x => x.Activity.Latitude.HasValue && x.Activity.Longitude.HasValue)
                .ToArray();

            if (located.Length < 2)
            {
                var unavailable = Enumerable.Range(0, timed.Length - 1).Select(index =>
                    new ActivityRouteLeg(timed[index].Id, timed[index].Title, timed[index + 1].Id, timed[index + 1].Title,
                        (int)(timed[index + 1].StartTime!.Value - timed[index].EndTime!.Value).TotalMinutes,
                        null, null, null, "unavailable",
                        "Add map coordinates to both activities to estimate this road leg.")).ToArray();
                dayResults.Add(new RouteDayAnalysis(day.Id, day.DayNumber, day.Date.ToString("yyyy-MM-dd"),
                    unavailable, "Add map coordinates to activities to estimate road routes."));
                continue;
            }

            var matrixIndexByActivityIndex = located
                .Select((item, matrixIndex) => new { item.ActivityIndex, matrixIndex })
                .ToDictionary(x => x.ActivityIndex, x => x.matrixIndex);

            var coordinates = string.Join(";", located.Select(x =>
                $"{x.Activity.Longitude!.Value.ToString(CultureInfo.InvariantCulture)},{x.Activity.Latitude!.Value.ToString(CultureInfo.InvariantCulture)}"));
            var url = $"table/v1/driving/{coordinates}?annotations=distance,duration";
            OsrmTableResponse? matrix;
            try
            {
                matrix = await client.GetFromJsonAsync<OsrmTableResponse>(url, ct);
            }
            catch (HttpRequestException)
            {
                return Results.Problem("The road-routing service is temporarily unavailable. No itinerary changes were made.", statusCode: 502);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Problem("The road-routing service timed out. No itinerary changes were made.", statusCode: 504);
            }

            var legs = new List<ActivityRouteLeg>();
            for (var index = 0; index < timed.Length - 1; index++)
            {
                var from = timed[index];
                var to = timed[index + 1];
                var gapMinutes = (int)(to.StartTime!.Value - from.EndTime!.Value).TotalMinutes;
                var hasFrom = matrixIndexByActivityIndex.TryGetValue(index, out var fromMatrix);
                var hasTo = matrixIndexByActivityIndex.TryGetValue(index + 1, out var toMatrix);

                if (!hasFrom || !hasTo)
                {
                    legs.Add(new ActivityRouteLeg(from.Id, from.Title, to.Id, to.Title, gapMinutes,
                        null, null, null, "unavailable",
                        "Add map coordinates to both activities to estimate this road leg."));
                    continue;
                }

                var seconds = matrix?.Durations?.ElementAtOrDefault(fromMatrix)?.ElementAtOrDefault(toMatrix);
                var metres = matrix?.Distances?.ElementAtOrDefault(fromMatrix)?.ElementAtOrDefault(toMatrix);
                if (seconds is null || metres is null || seconds <= 0 || metres <= 0)
                {
                    legs.Add(new ActivityRouteLeg(from.Id, from.Title, to.Id, to.Title, gapMinutes,
                        null, null, null, "unavailable", "No drivable route was returned for these coordinates."));
                    continue;
                }

                var roadMinutes = Math.Max(1, (int)Math.Ceiling(seconds.Value / 60d));
                var distanceKm = Math.Round(metres.Value / 1000d, 1);
                var requiredMinutes = roadMinutes + buffer;
                var status = gapMinutes < 0 ? "schedule_conflict"
                    : requiredMinutes > gapMinutes ? "tight" : "feasible";
                var explanation = status switch
                {
                    "schedule_conflict" => "The saved activity times overlap. A route estimate cannot resolve this scheduling conflict.",
                    "tight" => $"The {roadMinutes}-minute road estimate plus your {buffer}-minute buffer exceeds the {gapMinutes}-minute break.",
                    _ => $"The {gapMinutes}-minute break covers the {roadMinutes}-minute road estimate and {buffer}-minute buffer."
                };

                legs.Add(new ActivityRouteLeg(from.Id, from.Title, to.Id, to.Title, gapMinutes,
                    distanceKm, roadMinutes, requiredMinutes, status, explanation));
            }

            dayResults.Add(new RouteDayAnalysis(day.Id, day.DayNumber, day.Date.ToString("yyyy-MM-dd"), legs, null));
        }

        return Results.Ok(new RouteAnalysisResponse(
            tripId, "driving", buffer, dayResults,
            "Road route estimates use OpenStreetMap OSRM and saved activity coordinates. They are not live traffic ETAs and exclude parking, walking inside venues, queues, opening hours and public transport. A feasible result is a planning signal, not a guarantee."));
    }
}

public sealed record OsrmTableResponse(double?[][]? Durations, double?[][]? Distances);
public sealed record ActivityRouteLeg(
    Guid FromActivityId, string FromTitle, Guid ToActivityId, string ToTitle,
    int GapMinutes, double? DistanceKm, int? RoadDurationMinutes,
    int? RequiredMinutes, string Status, string Explanation);
public sealed record RouteDayAnalysis(
    Guid DayId, int DayNumber, string Date, ActivityRouteLeg[] Legs, string? Message);
public sealed record RouteAnalysisResponse(
    Guid TripId, string TravelMode, int BufferMinutes,
    IReadOnlyList<RouteDayAnalysis> Days, string Disclaimer);
