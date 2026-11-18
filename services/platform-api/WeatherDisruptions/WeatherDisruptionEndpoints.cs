using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.WeatherDisruptions;

public static class WeatherDisruptionEndpoints
{
    private const int MaximumActivitiesPerScan = 40;
    private const int MaximumUniqueLocationsPerScan = 12;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWeatherDisruptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/weather-disruptions")
            .WithTags("Weather disruption intelligence")
            .RequireAuthorization();
        group.MapPost("/scan", ScanAsync).WithName("ScanTripWeatherDisruptions");
        group.MapGet("/", ListAsync).WithName("ListTripWeatherAssessments");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<bool> IsMemberAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct)
        || await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var history = await db.TripWeatherAssessments.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.ForecastFetchedAt)
            .Take(500)
            .ToListAsync(ct);
        var latest = history
            .GroupBy(x => new { x.ActivityId, x.ActivityDate })
            .Select(group => ToResponse(group.First()))
            .OrderBy(x => x.ActivityDate)
            .ToArray();
        return Results.Ok(latest);
    }

    private static async Task<IResult> ScanAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db,
        IHttpClientFactory clients, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tripId, ct);
        if (trip is null) return Results.NotFound();

        var itineraryId = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!itineraryId.HasValue)
            return Results.Conflict(new { code = "itinerary_missing", message = "Create an itinerary and add activities before scanning weather." });

        var activities = await (
            from day in db.ItineraryDays.AsNoTracking()
            join activity in db.Activities.AsNoTracking() on day.Id equals activity.ItineraryDayId
            where day.ItineraryId == itineraryId.Value
            orderby day.Date, activity.SortOrder
            select new ActivityInput(activity.Id, activity.Title, activity.PlaceName,
                activity.Latitude, activity.Longitude, day.Date)
        ).Take(MaximumActivitiesPerScan + 1).ToListAsync(ct);

        if (activities.Count > MaximumActivitiesPerScan)
            return Results.Conflict(new { code = "scan_limit_exceeded", message = $"A scan supports up to {MaximumActivitiesPerScan} activities. Split this itinerary into smaller scans." });

        if (activities.Count == 0)
            return Results.Ok(new WeatherScanResponse(tripId, DateTimeOffset.UtcNow,
                "no_activities", [], [], 0, 0,
                "There are no itinerary activities to assess yet."));

        var eligible = activities.Where(x => x.Latitude.HasValue && x.Longitude.HasValue).ToArray();
        var skipped = activities.Where(x => !x.Latitude.HasValue || !x.Longitude.HasValue)
            .Select(x => new WeatherSkippedActivity(x.Id, x.Title, x.ActivityDate,
                "Add latitude and longitude to this activity to assess its local forecast."))
            .ToList();

        var locationGroups = eligible.GroupBy(x => LocationKey(x.Latitude!.Value, x.Longitude!.Value)).ToArray();
        if (locationGroups.Length > MaximumUniqueLocationsPerScan)
            return Results.Conflict(new { code = "location_limit_exceeded", message = $"A scan supports up to {MaximumUniqueLocationsPerScan} distinct coordinate locations. Add precise coordinates or split the scan." });

        var client = clients.CreateClient("weather-forecast");
        var fetchedAt = DateTimeOffset.UtcNow;
        var assessments = new List<TripWeatherAssessment>();
        var assessedActivities = new HashSet<Guid>();
        var forecastDaysAvailable = 0;

        foreach (var group in locationGroups)
        {
            ct.ThrowIfCancellationRequested();
            var first = group.First();
            ForecastResponse? forecast;
            try
            {
                forecast = await FetchForecastAsync(client, first.Latitude!.Value, first.Longitude!.Value, ct);
            }
            catch (HttpRequestException)
            {
                foreach (var activity in group)
                    skipped.Add(new WeatherSkippedActivity(activity.Id, activity.Title, activity.ActivityDate,
                        "The weather provider could not be reached. Retry the scan later."));
                continue;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                foreach (var activity in group)
                    skipped.Add(new WeatherSkippedActivity(activity.Id, activity.Title, activity.ActivityDate,
                        "The weather provider timed out. Retry the scan later."));
                continue;
            }
            catch (JsonException)
            {
                foreach (var activity in group)
                    skipped.Add(new WeatherSkippedActivity(activity.Id, activity.Title, activity.ActivityDate,
                        "The weather provider returned an unreadable forecast. Retry the scan later."));
                continue;
            }

            if (forecast is null)
            {
                foreach (var activity in group)
                    skipped.Add(new WeatherSkippedActivity(activity.Id, activity.Title, activity.ActivityDate,
                        "No forecast data was returned for this location."));
                continue;
            }

            forecastDaysAvailable = Math.Max(forecastDaysAvailable, forecast.Days.Count);
            var byDate = forecast.Days.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.First());
            foreach (var activity in group)
            {
                if (!byDate.TryGetValue(activity.ActivityDate, out var day))
                {
                    skipped.Add(new WeatherSkippedActivity(activity.Id, activity.Title, activity.ActivityDate,
                        "This date is outside the weather provider's available short-range forecast window."));
                    continue;
                }

                var assessment = Assess(tripId, activity, day, fetchedAt);
                assessments.Add(assessment);
                assessedActivities.Add(activity.Id);
            }
        }

        if (assessments.Count > 0)
        {
            db.TripWeatherAssessments.AddRange(assessments);
            await db.SaveChangesAsync(ct);
        }

        var high = assessments.Count(x => x.Severity == "high");
        var moderate = assessments.Count(x => x.Severity == "moderate");
        var message = assessments.Count == 0
            ? "No activity could be assessed. Add coordinates and choose dates within the available forecast window."
            : skipped.Count > 0
                ? $"Assessed {assessments.Count} activities; {skipped.Count} were skipped. Review the reasons before relying on this report."
                : $"Weather assessment completed for {assessments.Count} activities. Check high and moderate signals before travelling.";

        return Results.Ok(new WeatherScanResponse(
            tripId, fetchedAt, skipped.Count == 0 ? "complete" : assessments.Count == 0 ? "unavailable" : "partial",
            assessments.Select(ToResponse).ToArray(), skipped.ToArray(),
            high, moderate, message));
    }

    private static async Task<ForecastResponse?> FetchForecastAsync(
        HttpClient client, decimal latitude, decimal longitude, CancellationToken ct)
    {
        var lat = latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("0.####", CultureInfo.InvariantCulture);
        var path = "forecast?latitude=" + lat + "&longitude=" + lon
            + "&daily=weather_code,precipitation_probability_max,precipitation_sum,wind_speed_10m_max,temperature_2m_max"
            + "&forecast_days=16&past_days=1&timezone=auto";
        using var response = await client.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (!root.TryGetProperty("daily", out var daily)
            || !daily.TryGetProperty("time", out var dates)
            || dates.ValueKind != JsonValueKind.Array)
            return null;

        var days = new List<ForecastDay>();
        for (var i = 0; i < dates.GetArrayLength(); i++)
        {
            var dateText = dates[i].GetString();
            if (!DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            days.Add(new ForecastDay(
                date,
                ReadInt(daily, "weather_code", i) ?? 0,
                ReadDecimal(daily, "precipitation_probability_max", i),
                ReadDecimal(daily, "precipitation_sum", i),
                ReadDecimal(daily, "wind_speed_10m_max", i),
                ReadDecimal(daily, "temperature_2m_max", i)));
        }
        return new ForecastResponse(days);
    }

    private static int? ReadInt(JsonElement parent, string name, int index)
    {
        if (!parent.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array || index >= values.GetArrayLength())
            return null;
        return values[index].ValueKind == JsonValueKind.Number && values[index].TryGetInt32(out var value) ? value : null;
    }

    private static decimal? ReadDecimal(JsonElement parent, string name, int index)
    {
        if (!parent.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array || index >= values.GetArrayLength())
            return null;
        return values[index].ValueKind == JsonValueKind.Number && values[index].TryGetDecimal(out var value) ? value : null;
    }

    private static TripWeatherAssessment Assess(
        Guid tripId, ActivityInput activity, ForecastDay day, DateTimeOffset fetchedAt)
    {
        var code = day.WeatherCode;
        var thunderstorm = code >= 95;
        var snow = code is >= 71 and <= 77 or 85 or 86;
        var rain = code is >= 51 and <= 67 or >= 80 and <= 82;
        var fog = code is 45 or 48;
        var heavyRain = day.PrecipitationSumMm >= 30m
            || (day.PrecipitationProbabilityMax >= 80m && day.PrecipitationSumMm >= 10m);
        var strongWind = day.WindSpeedMaxKmh >= 50m;
        var moderateWind = day.WindSpeedMaxKmh >= 35m;
        var severity = thunderstorm || strongWind || heavyRain ? "high"
            : snow || rain || fog || moderateWind
                || (day.PrecipitationProbabilityMax >= 60m && day.PrecipitationSumMm >= 5m)
                ? "moderate" : "low";

        var condition = WeatherDescription(code);
        var summary = condition;
        if (day.PrecipitationProbabilityMax.HasValue)
            summary += $"; precipitation probability up to {day.PrecipitationProbabilityMax.Value:0}%";
        if (day.PrecipitationSumMm.HasValue)
            summary += $"; expected precipitation {day.PrecipitationSumMm.Value:0.#} mm";
        if (day.WindSpeedMaxKmh.HasValue)
            summary += $"; maximum wind {day.WindSpeedMaxKmh.Value:0.#} km/h";

        var recommendation = severity switch
        {
            "high" => "Review local official weather warnings and consider rescheduling exposed or outdoor activities. No itinerary changes have been made.",
            "moderate" => "Consider an indoor alternative or a flexible time slot, and recheck the forecast closer to the activity date.",
            _ => "No strong disruption signal was detected in this forecast. Recheck closer to the activity date."
        };

        return new TripWeatherAssessment
        {
            TripId = tripId,
            ActivityId = activity.Id,
            ActivityTitle = activity.Title,
            PlaceName = activity.PlaceName,
            ActivityDate = activity.ActivityDate,
            WeatherCode = code,
            PrecipitationProbabilityMax = day.PrecipitationProbabilityMax,
            PrecipitationSumMm = day.PrecipitationSumMm,
            WindSpeedMaxKmh = day.WindSpeedMaxKmh,
            TemperatureMaxC = day.TemperatureMaxC,
            Severity = severity,
            Summary = summary,
            Recommendation = recommendation,
            ForecastFetchedAt = fetchedAt
        };
    }

    private static string WeatherDescription(int code) => code switch
    {
        0 => "Clear sky forecast",
        1 => "Mainly clear forecast",
        2 => "Partly cloudy forecast",
        3 => "Overcast forecast",
        45 or 48 => "Fog forecast",
        51 or 53 or 55 => "Drizzle forecast",
        56 or 57 => "Freezing drizzle forecast",
        61 or 63 or 65 => "Rain forecast",
        66 or 67 => "Freezing rain forecast",
        71 or 73 or 75 or 77 => "Snow forecast",
        80 or 81 or 82 => "Rain showers forecast",
        85 or 86 => "Snow showers forecast",
        95 => "Thunderstorm forecast",
        96 or 99 => "Thunderstorm with hail forecast",
        _ => "Unclassified weather code " + code.ToString(CultureInfo.InvariantCulture)
    };

    private static string LocationKey(decimal latitude, decimal longitude) =>
        Math.Round(latitude, 4).ToString("0.0000", CultureInfo.InvariantCulture) + ","
        + Math.Round(longitude, 4).ToString("0.0000", CultureInfo.InvariantCulture);

    private static WeatherAssessmentResponse ToResponse(TripWeatherAssessment x) =>
        new(x.Id, x.ActivityId, x.ActivityTitle, x.PlaceName, x.ActivityDate,
            x.WeatherCode, x.PrecipitationProbabilityMax, x.PrecipitationSumMm,
            x.WindSpeedMaxKmh, x.TemperatureMaxC, x.Severity, x.Summary,
            x.Recommendation, x.Source, x.ForecastFetchedAt);

    private sealed record ActivityInput(Guid Id, string Title, string? PlaceName,
        decimal? Latitude, decimal? Longitude, DateOnly ActivityDate);
    private sealed record ForecastDay(DateOnly Date, int WeatherCode,
        decimal? PrecipitationProbabilityMax, decimal? PrecipitationSumMm,
        decimal? WindSpeedMaxKmh, decimal? TemperatureMaxC);
    private sealed record ForecastResponse(List<ForecastDay> Days);
}

public sealed record WeatherAssessmentResponse(
    Guid Id, Guid ActivityId, string ActivityTitle, string? PlaceName,
    DateOnly ActivityDate, int WeatherCode, decimal? PrecipitationProbabilityMax,
    decimal? PrecipitationSumMm, decimal? WindSpeedMaxKmh, decimal? TemperatureMaxC,
    string Severity, string Summary, string Recommendation, string Source,
    DateTimeOffset ForecastFetchedAt);

public sealed record WeatherSkippedActivity(Guid ActivityId, string ActivityTitle,
    DateOnly ActivityDate, string Reason);

public sealed record WeatherScanResponse(
    Guid TripId, DateTimeOffset ScannedAt, string Status,
    WeatherAssessmentResponse[] Assessments, WeatherSkippedActivity[] SkippedActivities,
    int HighSeverityCount, int ModerateSeverityCount, string Message);
