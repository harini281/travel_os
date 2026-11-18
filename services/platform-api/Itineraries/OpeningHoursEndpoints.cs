using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Itineraries;

public static class OpeningHoursEndpoints
{
    public static IEndpointRouteBuilder MapOpeningHoursEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/trips/{tripId:guid}/itinerary/opening-hours-analysis", AnalyzeOpeningHoursAsync)
            .RequireAuthorization()
            .RequireRateLimiting("opening-hours")
            .WithTags("Itineraries")
            .WithName("AnalyzeAttractionOpeningHours");
        return endpoints;
    }

    private static async Task<IResult> AnalyzeOpeningHoursAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db,
        IHttpClientFactory clients, CancellationToken ct)
    {
        var claim = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var userId)) return Results.Unauthorized();

        var isMember = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct)
            || await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!isMember) return Results.NotFound();

        var itinerary = await db.Itineraries.AsNoTracking()
            .Include(x => x.Days).ThenInclude(x => x.Activities)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        if (itinerary is null)
            return Results.Ok(new OpeningHoursAnalysisResponse(tripId, DateTimeOffset.UtcNow,
                Array.Empty<OpeningHoursDayResult>(),
                "Opening hours are sourced from OpenStreetMap community data. Missing or unsupported data is never treated as confirmed open."));

        var allActivities = itinerary.Days.SelectMany(x => x.Activities).ToArray();
        if (allActivities.Length > 40)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["activities"] = ["Opening-hours analysis supports up to 40 activities per request."]
            });

        var candidates = itinerary.Days
            .OrderBy(x => x.DayNumber)
            .SelectMany(day => day.Activities.Select(activity => new OpeningHoursCandidate(
                day.Id, day.DayNumber, day.Date, activity.Id, activity.Title, activity.PlaceName,
                activity.Latitude.HasValue ? (double?)activity.Latitude.Value : null,
                activity.Longitude.HasValue ? (double?)activity.Longitude.Value : null,
                activity.StartTime, activity.EndTime)))
            .Where(x => !string.IsNullOrWhiteSpace(x.PlaceName) && x.Latitude.HasValue && x.Longitude.HasValue)
            .Take(40)
            .ToArray();

        var places = new List<OpenStreetMapPlace>();
        if (candidates.Length > 0)
        {
            var query = BuildOverpassQuery(candidates);
            try
            {
                using var response = await clients.CreateClient("overpass").PostAsync(
                    "", new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) }), ct);
                if (!response.IsSuccessStatusCode)
                    return Results.Problem("The opening-hours data service is temporarily unavailable. No opening status was assumed.", statusCode: 502);

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (document.RootElement.TryGetProperty("elements", out var elements))
                {
                    foreach (var element in elements.EnumerateArray())
                    {
                        if (!element.TryGetProperty("tags", out var tags)) continue;
                        var name = ReadTag(tags, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var lat = ReadNumber(element, "lat") ?? ReadNestedNumber(element, "center", "lat");
                        var lon = ReadNumber(element, "lon") ?? ReadNestedNumber(element, "center", "lon");
                        if (!lat.HasValue || !lon.HasValue) continue;
                        places.Add(new OpenStreetMapPlace(name, lat.Value, lon.Value,
                            ReadTag(tags, "opening_hours"), ReadTag(tags, "website"), ReadTag(tags, "phone")));
                    }
                }
            }
            catch (HttpRequestException)
            {
                return Results.Problem("The opening-hours data service could not be reached. No opening status was assumed.", statusCode: 502);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Problem("The opening-hours data service timed out. No opening status was assumed.", statusCode: 504);
            }
            catch (JsonException)
            {
                return Results.Problem("The opening-hours data service returned an unreadable response. No opening status was assumed.", statusCode: 502);
            }
        }

        var results = itinerary.Days.OrderBy(x => x.DayNumber).Select(day =>
        {
            var dayItems = itinerary.Days.First(x => x.Id == day.Id).Activities.Select(activity =>
            {
                var candidate = new OpeningHoursCandidate(day.Id, day.DayNumber, day.Date, activity.Id,
                    activity.Title, activity.PlaceName,
                    activity.Latitude.HasValue ? (double?)activity.Latitude.Value : null,
                    activity.Longitude.HasValue ? (double?)activity.Longitude.Value : null,
                    activity.StartTime, activity.EndTime);

                if (string.IsNullOrWhiteSpace(candidate.PlaceName) || !candidate.Latitude.HasValue || !candidate.Longitude.HasValue)
                    return Result(candidate, "missing_location", null, null,
                        "Add a place name and map coordinates before checking opening hours.");

                if (!candidate.StartTime.HasValue || !candidate.EndTime.HasValue)
                    return Result(candidate, "missing_schedule", null, null,
                        "Add activity start and end times to compare the visit with opening hours.");

                var place = places
                    .Where(x => string.Equals(Normalize(x.Name), Normalize(candidate.PlaceName), StringComparison.Ordinal))
                    .Select(x => new { Place = x, Distance = DistanceMetres(candidate.Latitude.Value, candidate.Longitude.Value, x.Latitude, x.Longitude) })
                    .Where(x => x.Distance <= 250)
                    .OrderBy(x => x.Distance)
                    .Select(x => x.Place)
                    .FirstOrDefault();

                if (place is null)
                    return Result(candidate, "unavailable", null, null,
                        "No matching OpenStreetMap place was found near the saved coordinates. Verify the venue manually.");

                if (string.IsNullOrWhiteSpace(place.OpeningHours))
                    return Result(candidate, "no_hours_data", place.Name, null,
                        "The place was matched, but no opening_hours data is published. Check the venue's official source.");

                if (!OpeningHoursParser.TryGetIntervals(place.OpeningHours, candidate.Date.DayOfWeek, out var intervals, out var closed))
                    return Result(candidate, "manual_review", place.Name, place.OpeningHours,
                        "The published opening-hours expression uses rules this checker does not support. Verify the venue's official source.");

                if (closed)
                    return Result(candidate, "closed", place.Name, place.OpeningHours,
                        "The published weekly schedule marks this day as closed. Check for special holiday hours.");

                var fits = intervals.Any(x => candidate.StartTime.Value >= x.Start && candidate.EndTime.Value <= x.End);
                return fits
                    ? Result(candidate, "open_during_activity", place.Name, place.OpeningHours,
                        "The planned activity time fits within the published weekly opening-hours interval. Special closures may still apply.")
                    : Result(candidate, "outside_opening_hours", place.Name, place.OpeningHours,
                        "The planned activity does not fit within a published opening-hours interval. Review the time or confirm special hours with the venue.");
            }).ToArray();

            return new OpeningHoursDayResult(day.Id, day.DayNumber, day.Date.ToString("yyyy-MM-dd"), dayItems);
        }).ToArray();

        return Results.Ok(new OpeningHoursAnalysisResponse(
            tripId, DateTimeOffset.UtcNow, results,
            "Opening hours are sourced from OpenStreetMap community data and may be incomplete or outdated. Only simple weekly schedules are interpreted. Public holidays, exceptions, seasonal hours and unsupported expressions require manual verification. This is not a reservation or guarantee of admission."));
    }

    private static OpeningHoursActivityResult Result(
        OpeningHoursCandidate item, string status, string? matchedPlace, string? rawHours, string recommendation) =>
        new(item.ActivityId, item.ActivityTitle, item.PlaceName, item.Date.ToString("yyyy-MM-dd"),
            item.StartTime?.ToString("HH:mm"), item.EndTime?.ToString("HH:mm"),
            status, matchedPlace, rawHours, recommendation);

    private static string BuildOverpassQuery(IEnumerable<OpeningHoursCandidate> candidates)
    {
        var queries = candidates.Select(x =>
        {
            var lat = x.Latitude!.Value.ToString(CultureInfo.InvariantCulture);
            var lon = x.Longitude!.Value.ToString(CultureInfo.InvariantCulture);
            var escaped = Regex.Replace(x.PlaceName!, @"[.*+?^${}()|\[\]\\]", @"\$&");
            var name = Regex.Replace(escaped, @"\s+", " ").Trim();
            return $"""
                node(around:250,{lat},{lon})["name"~"^{name}$",i];
                way(around:250,{lat},{lon})["name"~"^{name}$",i];
                relation(around:250,{lat},{lon})["name"~"^{name}$",i];
                """;
        });
        return "[out:json][timeout:25];(" + string.Join("\n", queries) + ");out center tags;";
    }

    private static string? ReadTag(JsonElement tags, string key) =>
        tags.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadNumber(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) ? number : null;

    private static double? ReadNestedNumber(JsonElement element, string parent, string key) =>
        element.TryGetProperty(parent, out var nested) ? ReadNumber(nested, key) : null;

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");

    private static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private sealed record OpeningHoursCandidate(
        Guid DayId, int DayNumber, DateOnly Date, Guid ActivityId, string ActivityTitle,
        string? PlaceName, double? Latitude, double? Longitude, TimeOnly? StartTime, TimeOnly? EndTime);
    private sealed record OpenStreetMapPlace(
        string Name, double Latitude, double Longitude, string? OpeningHours, string? Website, string? Phone);
}

internal static class OpeningHoursParser
{
    private static readonly string[] Days = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];
    private static readonly Regex DayPrefix = new(
        @"^((?:(?:Mo|Tu|We|Th|Fr|Sa|Su)(?:-(?:Mo|Tu|We|Th|Fr|Sa|Su))?)(?:,(?:(?:Mo|Tu|We|Th|Fr|Sa|Su)(?:-(?:Mo|Tu|We|Th|Fr|Sa|Su))?))*)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryGetIntervals(string expression, DayOfWeek day, out List<(TimeOnly Start, TimeOnly End)> intervals, out bool closed)
    {
        intervals = new();
        closed = false;
        if (expression.Trim() == "24/7")
        {
            intervals.Add((TimeOnly.MinValue, TimeOnly.MaxValue));
            return true;
        }

        var dayCode = day switch
        {
            DayOfWeek.Monday => "Mo", DayOfWeek.Tuesday => "Tu", DayOfWeek.Wednesday => "We",
            DayOfWeek.Thursday => "Th", DayOfWeek.Friday => "Fr", DayOfWeek.Saturday => "Sa", _ => "Su"
        };
        var matchedRule = false;
        var sawDaySpecificRule = false;
        var matchingRuleCount = 0;
        foreach (var rawRule in expression.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var rule = rawRule.Trim();
            if (rule.Contains("PH", StringComparison.OrdinalIgnoreCase)
                || rule.Contains("SH", StringComparison.OrdinalIgnoreCase)
                || rule.Contains("sunrise", StringComparison.OrdinalIgnoreCase)
                || rule.Contains("sunset", StringComparison.OrdinalIgnoreCase)
                || rule.Contains("week", StringComparison.OrdinalIgnoreCase))
                return false;

            var match = DayPrefix.Match(rule);
            string hours;
            var applies = true;
            if (match.Success)
            {
                sawDaySpecificRule = true;
                applies = ExpandDays(match.Groups[1].Value).Contains(dayCode);
                hours = match.Groups[2].Value.Trim();
            }
            else
            {
                hours = rule;
            }
            if (!applies) continue;
            matchingRuleCount++;
            if (matchingRuleCount > 1) return false;
            matchedRule = true;
            if (hours.Equals("off", StringComparison.OrdinalIgnoreCase)
                || hours.Equals("closed", StringComparison.OrdinalIgnoreCase))
            {
                closed = true;
                intervals.Clear();
                continue;
            }

            foreach (var period in hours.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = period.Split('-', StringSplitOptions.TrimEntries);
                if (pieces.Length != 2
                    || !TimeOnly.TryParseExact(pieces[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                    || !TimeOnly.TryParseExact(pieces[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                    || end <= start)
                    return false;
                intervals.Add((start, end));
            }
        }
        if (!matchedRule && sawDaySpecificRule)
        {
            closed = true;
            return true;
        }
        return matchedRule;
    }

    private static HashSet<string> ExpandDays(string selector)
    {
        var result = new HashSet<string>();
        foreach (var part in selector.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            var start = Array.IndexOf(Days, range[0]);
            var end = range.Length == 2 ? Array.IndexOf(Days, range[1]) : start;
            if (start < 0 || end < 0) continue;
            for (var index = start; ; index = (index + 1) % 7)
            {
                result.Add(Days[index]);
                if (index == end) break;
            }
        }
        return result;
    }
}

public sealed record OpeningHoursActivityResult(
    Guid ActivityId, string ActivityTitle, string? PlaceName, string ActivityDate,
    string? StartTime, string? EndTime, string Status, string? MatchedPlace,
    string? PublishedOpeningHours, string Recommendation);
public sealed record OpeningHoursDayResult(
    Guid DayId, int DayNumber, string Date, OpeningHoursActivityResult[] Activities);
public sealed record OpeningHoursAnalysisResponse(
    Guid TripId, DateTimeOffset CheckedAt, IReadOnlyList<OpeningHoursDayResult> Days, string Disclaimer);
