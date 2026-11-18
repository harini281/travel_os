using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.GroundTransfers;

public static class GroundTransferEndpoints
{
    public static IEndpointRouteBuilder MapGroundTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/trips/{tripId:guid}/ground-transfers/estimate", EstimateAsync)
            .RequireAuthorization()
            .RequireRateLimiting("ground-transfer")
            .WithTags("Airport and ground transfers")
            .WithName("EstimateAirportGroundTransfer");
        return endpoints;
    }

    private static async Task<IResult> EstimateAsync(
        Guid tripId, GroundTransferRequest request, ClaimsPrincipal principal,
        TravelOsDbContext db, IAirportTransferRouter router, CancellationToken ct)
    {
        var claim = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var userId)) return Results.Unauthorized();
        var member = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct)
            || await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!member) return Results.NotFound();

        var airport = request.AirportIataCode?.Trim().ToUpperInvariant() ?? "";
        var destination = request.Destination?.Trim() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(airport, "^[A-Z]{3}$"))
            return Results.ValidationProblem(new Dictionary<string,string[]> { ["airportIataCode"] = ["Enter a three-letter IATA airport code."] });
        if (destination.Length is < 4 or > 240)
            return Results.ValidationProblem(new Dictionary<string,string[]> { ["destination"] = ["Enter a destination address or place name (4–240 characters)."] });
        if (request.PickupBufferMinutes is < 0 or > 240)
            return Results.ValidationProblem(new Dictionary<string,string[]> { ["pickupBufferMinutes"] = ["Pickup buffer must be between 0 and 240 minutes."] });

        try
        {
            var result = await router.EstimateAsync(airport, destination, request.TravelMode, ct);
            if (result is null)
                return Results.NotFound(new { code = "route_not_found", message = "A road route could not be found. Check the airport code and destination." });
            var pickup = result.DurationMinutes + request.PickupBufferMinutes;
            var recommended = request.ArrivalUtc?.AddMinutes(pickup);
            return Results.Ok(new GroundTransferEstimateResponse(
                airport, result.AirportName, result.AirportLatitude, result.AirportLongitude,
                destination, result.DestinationLatitude, result.DestinationLongitude,
                request.TravelMode, result.DistanceKm, result.DurationMinutes,
                request.PickupBufferMinutes, pickup, request.ArrivalUtc, recommended,
                result.Provider, DateTimeOffset.UtcNow,
                "Road travel estimate only. Traffic, queues, parking, terminal access, immigration, baggage collection and airport pickup rules may add substantial time. This is not a live traffic ETA or a booked transfer."));
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The mapping service is temporarily unavailable. No transfer was booked.", statusCode: 502);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem("The mapping service timed out. No transfer was booked.", statusCode: 504);
        }
    }
}

public interface IAirportTransferRouter
{
    Task<AirportRouteResult?> EstimateAsync(string airportIataCode, string destination, string mode, CancellationToken ct);
}

public sealed class OpenStreetMapTransferRouter(HttpClient geocoder, HttpClient router) : IAirportTransferRouter
{
    public async Task<AirportRouteResult?> EstimateAsync(string airportIataCode, string destination, string mode, CancellationToken ct)
    {
        var airportQuery = Uri.EscapeDataString(airportIataCode + " airport");
        var airportMatches = await geocoder.GetFromJsonAsync<List<NominatimPlace>>(
            $"search?format=jsonv2&limit=5&q={airportQuery}", ct);
        var airport = airportMatches?.FirstOrDefault(x =>
            (x.Type?.Contains("aeroway", StringComparison.OrdinalIgnoreCase) == true)
            || x.Class?.Contains("aeroway", StringComparison.OrdinalIgnoreCase) == true)
            ?? airportMatches?.FirstOrDefault();
        if (airport is null || !TryCoordinate(airport.Lat, out var airportLat) || !TryCoordinate(airport.Lon, out var airportLon))
            return null;

        var destinationQuery = Uri.EscapeDataString(destination);
        var destinationMatches = await geocoder.GetFromJsonAsync<List<NominatimPlace>>(
            $"search?format=jsonv2&limit=1&q={destinationQuery}", ct);
        var place = destinationMatches?.FirstOrDefault();
        if (place is null || !TryCoordinate(place.Lat, out var destLat) || !TryCoordinate(place.Lon, out var destLon))
            return null;

        var profile = mode == "cycling" ? "cycling" : "driving";
        var route = await router.GetFromJsonAsync<OsrmRouteResponse>(
            $"route/v1/{profile}/{airportLon.ToString(CultureInfo.InvariantCulture)},{airportLat.ToString(CultureInfo.InvariantCulture)};{destLon.ToString(CultureInfo.InvariantCulture)},{destLat.ToString(CultureInfo.InvariantCulture)}?overview=false&alternatives=false&steps=false", ct);
        var best = route?.Routes?.FirstOrDefault();
        if (best is null || best.Distance <= 0 || best.Duration <= 0) return null;

        return new AirportRouteResult(
            airport.DisplayName ?? airportIataCode, airportLat, airportLon,
            destLat, destLon, Math.Round(best.Distance / 1000, 1),
            Math.Max(1, (int)Math.Ceiling(best.Duration / 60)),
            "OpenStreetMap Nominatim + OSRM");
    }

    private static bool TryCoordinate(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && value is >= -180 and <= 180;

    private sealed record NominatimPlace(string? Lat, string? Lon, string? DisplayName, string? Type, string? Class);
    private sealed record OsrmRouteResponse(List<OsrmRoute>? Routes);
    private sealed record OsrmRoute(double Distance, double Duration);
}

public sealed record GroundTransferRequest(
    string AirportIataCode, string Destination, string TravelMode = "driving",
    int PickupBufferMinutes = 30, DateTimeOffset? ArrivalUtc = null);

public sealed record AirportRouteResult(
    string AirportName, double AirportLatitude, double AirportLongitude,
    double DestinationLatitude, double DestinationLongitude, double DistanceKm,
    int DurationMinutes, string Provider);

public sealed record GroundTransferEstimateResponse(
    string AirportIataCode, string AirportName, double AirportLatitude, double AirportLongitude,
    string Destination, double DestinationLatitude, double DestinationLongitude,
    string TravelMode, double DistanceKm, int RoadDurationMinutes,
    int PickupBufferMinutes, int TotalPlanningMinutes, DateTimeOffset? ArrivalUtc,
    DateTimeOffset? RecommendedDepartureUtc, string Provider, DateTimeOffset EstimatedAt,
    string Disclaimer);
