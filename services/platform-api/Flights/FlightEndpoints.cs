using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Flights;

public static class FlightEndpoints
{
    public static IEndpointRouteBuilder MapFlightEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/flights")
            .WithTags("Flight and journey disruptions")
            .RequireAuthorization();
        group.MapGet("/", ListAsync).WithName("ListTripFlights");
        group.MapGet("/connections", ConnectionsAsync).WithName("AssessFlightConnections");
        group.MapPost("/", CreateAsync).WithName("CreateTripFlight");
        group.MapPost("/{flightId:guid}/refresh", RefreshAsync)
            .RequireRateLimiting("flight-refresh")
            .WithName("RefreshTripFlightStatus");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<bool> IsMemberAsync(Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct)
        || await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var flights = await db.TripFlights.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderBy(x => x.ScheduledDepartureUtc)
            .ToListAsync(ct);
        return Results.Ok(flights.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> ConnectionsAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var flights = await db.TripFlights.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderBy(x => x.ScheduledDepartureUtc)
            .ToListAsync(ct);

        var connections = flights
            .GroupBy(x => x.JourneyGroup, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var legs = group.OrderBy(x => x.ScheduledDepartureUtc).ToArray();
                return Enumerable.Range(0, Math.Max(0, legs.Length - 1))
                    .Select(index => AssessConnection(legs[index], legs[index + 1]));
            })
            .OrderBy(x => x.InboundScheduledArrivalUtc)
            .ToArray();

        return Results.Ok(new FlightConnectionsResponse(
            tripId, DateTimeOffset.UtcNow, connections,
            "Connections are heuristic schedule checks, not airline minimum connection times or protected-connection guarantees. Confirm airport, terminal, immigration, baggage and transfer requirements with the airline and airport."));
    }

    private static FlightConnectionAssessment AssessConnection(TripFlight inbound, TripFlight outbound)
    {
        var arrival = inbound.ActualArrivalUtc ?? inbound.EstimatedArrivalUtc ?? inbound.ScheduledArrivalUtc;
        var departure = outbound.ActualDepartureUtc ?? outbound.EstimatedDepartureUtc ?? outbound.ScheduledDepartureUtc;
        var gap = (int)Math.Round((departure - arrival).TotalMinutes);
        var sameAirport = string.Equals(inbound.DestinationAirportCode, outbound.OriginAirportCode, StringComparison.OrdinalIgnoreCase);
        var disrupted = inbound.Status is "cancelled" or "diverted" || outbound.Status is "cancelled" or "diverted";
        var timeSource = inbound.ActualArrivalUtc.HasValue || inbound.EstimatedArrivalUtc.HasValue
            || outbound.ActualDepartureUtc.HasValue || outbound.EstimatedDepartureUtc.HasValue
            ? "provider_estimate_or_actual" : "scheduled_only";

        string risk;
        string explanation;
        int? heuristicBuffer = null;
        if (disrupted)
        {
            risk = "unassessable";
            explanation = "One of these flights is reported cancelled or diverted. The planned connection cannot be assessed from the saved schedule.";
        }
        else if (!sameAirport)
        {
            risk = "airport_change";
            explanation = $"Airport change required: arrive at {inbound.DestinationAirportCode}, depart from {outbound.OriginAirportCode}. Ground transfer time and feasibility are unknown.";
        }
        else if (gap < 0)
        {
            risk = "overlap";
            explanation = "The next departure is scheduled before the previous arrival (or provider-updated times overlap). Treat this as an infeasible connection until verified.";
            heuristicBuffer = 120;
        }
        else
        {
            heuristicBuffer = 120;
            if (gap < 60)
            {
                risk = "very_tight";
                explanation = "Less than 60 minutes between flights. This is a high-risk schedule signal; terminal, immigration and boarding requirements are not included.";
            }
            else if (gap < 120)
            {
                risk = "tight";
                explanation = "Between 60 and 119 minutes between flights. This may be insufficient depending on airport, terminals, immigration and airline rules.";
            }
            else if (gap < 180)
            {
                risk = "moderate";
                explanation = "Between 2 and 3 hours are scheduled. Verify the airline's minimum connection time and airport procedures.";
            }
            else
            {
                risk = "more_time";
                explanation = "At least 3 hours are scheduled. This is more time than shorter connections, but does not guarantee feasibility.";
            }
        }

        return new FlightConnectionAssessment(
            inbound.Id, inbound.JourneyGroup, inbound.FlightIdentifier,
            inbound.OriginAirportCode, inbound.DestinationAirportCode,
            inbound.ScheduledArrivalUtc, arrival, inbound.Status,
            outbound.Id, outbound.FlightIdentifier, outbound.OriginAirportCode,
            outbound.DestinationAirportCode, outbound.ScheduledDepartureUtc,
            departure, outbound.Status, gap, sameAirport, risk,
            heuristicBuffer, timeSource, explanation);
    }

    private static async Task<IResult> CreateAsync(
        Guid tripId, CreateTripFlightRequest request, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        var identifier = request.FlightIdentifier?.Trim().ToUpperInvariant().Replace(" ", "") ?? "";
        var journeyGroup = string.IsNullOrWhiteSpace(request.JourneyGroup) ? "Main journey" : request.JourneyGroup.Trim();
        var origin = request.OriginAirportCode?.Trim().ToUpperInvariant() ?? "";
        var destination = request.DestinationAirportCode?.Trim().ToUpperInvariant() ?? "";
        if (identifier.Length is < 2 or > 24 || !System.Text.RegularExpressions.Regex.IsMatch(identifier, "^[A-Z0-9-]+$"))
            errors["flightIdentifier"] = ["Enter a valid flight number, for example UL 503 or SQ 468."];
        if (journeyGroup.Length > 80)
            errors["journeyGroup"] = ["Journey group must be at most 80 characters. Use a label such as Outbound or Return."];
        if (!System.Text.RegularExpressions.Regex.IsMatch(origin, "^[A-Z]{3}$"))
            errors["originAirportCode"] = ["Use a three-letter IATA airport code."];
        if (!System.Text.RegularExpressions.Regex.IsMatch(destination, "^[A-Z]{3}$"))
            errors["destinationAirportCode"] = ["Use a three-letter IATA airport code."];
        if (origin.Length == 3 && origin == destination)
            errors["destinationAirportCode"] = ["Origin and destination airports must be different."];
        if (request.ScheduledArrivalUtc <= request.ScheduledDepartureUtc)
            errors["scheduledArrivalUtc"] = ["Scheduled arrival must be after scheduled departure."];
        if ((request.ScheduledArrivalUtc - request.ScheduledDepartureUtc).TotalHours > 36)
            errors["scheduledArrivalUtc"] = ["A single flight duration cannot exceed 36 hours."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var flight = new TripFlight
        {
            TripId = tripId,
            CreatedByUserId = userId,
            FlightIdentifier = identifier.Replace(" ", ""),
            JourneyGroup = journeyGroup,
            OriginAirportCode = origin,
            DestinationAirportCode = destination,
            ScheduledDepartureUtc = request.ScheduledDepartureUtc.ToUniversalTime(),
            ScheduledArrivalUtc = request.ScheduledArrivalUtc.ToUniversalTime()
        };
        db.TripFlights.Add(flight);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "duplicate_flight", message = "This flight and scheduled departure are already saved for the trip." });
        }
        return Results.Created($"/api/v1/trips/{tripId}/flights/{flight.Id}", ToResponse(flight));
    }

    private static async Task<IResult> RefreshAsync(
        Guid tripId, Guid flightId, ClaimsPrincipal principal, TravelOsDbContext db,
        IFlightStatusProvider provider, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var flight = await db.TripFlights.FirstOrDefaultAsync(x => x.Id == flightId && x.TripId == tripId, ct);
        if (flight is null) return Results.NotFound();

        if (!provider.IsConfigured)
            return Results.Conflict(new
            {
                code = "flight_provider_not_configured",
                provider = provider.Name,
                message = "Flight tracking is not connected yet. Configure FLIGHTAWARE_API_KEY on the server to request provider-confirmed status. Your saved flight details are unchanged."
            });

        FlightProviderResult result;
        try
        {
            result = await provider.GetFlightAsync(
                flight.FlightIdentifier,
                flight.ScheduledDepartureUtc.AddHours(-12),
                flight.ScheduledArrivalUtc.AddHours(24),
                ct);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The flight status provider could not be reached. No flight status was changed.", statusCode: 502);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem("The flight status provider timed out. No flight status was changed.", statusCode: 504);
        }
        if (result.Outcome == "not_found")
            return Results.NotFound(new { code = "flight_not_found_by_provider", message = "The provider returned no matching flight for this identifier and schedule. Verify the flight number and date." });
        if (result.Outcome != "found")
            return Results.Problem(result.Message ?? "The provider could not confirm flight status.", statusCode: 502);

        flight.Status = result.Status;
        flight.Provider = result.Provider;
        flight.ProviderFlightId = result.ProviderFlightId;
        flight.ProviderStatus = result.ProviderStatus;
        flight.EstimatedDepartureUtc = result.EstimatedDepartureUtc;
        flight.ActualDepartureUtc = result.ActualDepartureUtc;
        flight.EstimatedArrivalUtc = result.EstimatedArrivalUtc;
        flight.ActualArrivalUtc = result.ActualArrivalUtc;
        flight.DepartureDelayMinutes = result.DepartureDelayMinutes;
        flight.LastCheckedAt = DateTimeOffset.UtcNow;
        flight.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var departureDate = DateOnly.FromDateTime(flight.ScheduledDepartureUtc.UtcDateTime);
        var arrivalDate = DateOnly.FromDateTime(flight.ScheduledArrivalUtc.UtcDateTime);
        var itineraryId = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId).OrderByDescending(x => x.Version)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var departureActivities = 0;
        var arrivalActivities = 0;
        if (itineraryId.HasValue)
        {
            var days = await db.ItineraryDays.AsNoTracking()
                .Where(x => x.ItineraryId == itineraryId.Value && (x.Date == departureDate || x.Date == arrivalDate))
                .Select(x => new { x.Date, ActivityCount = x.Activities.Count })
                .ToListAsync(ct);
            departureActivities = days.Where(x => x.Date == departureDate).Sum(x => x.ActivityCount);
            arrivalActivities = days.Where(x => x.Date == arrivalDate).Sum(x => x.ActivityCount);
        }

        var disruption = flight.Status is "delayed" or "cancelled" or "diverted";
        var impact = disruption && (departureActivities + arrivalActivities) > 0
            ? $"Review {departureActivities} planned activity/activities on the departure date and {arrivalActivities} on the arrival date. Activity times are not stored, so this is a same-day review signal, not a confirmed time overlap."
            : "No same-day itinerary activities were found, or no disruption signal was reported. This check does not confirm ground-transfer or connection feasibility.";
        return Results.Ok(new FlightRefreshResponse(ToResponse(flight), disruption, departureActivities, arrivalActivities, impact));
    }

    private static TripFlightResponse ToResponse(TripFlight x) => new(
        x.Id, x.TripId, x.FlightIdentifier, x.JourneyGroup, x.OriginAirportCode, x.DestinationAirportCode,
        x.ScheduledDepartureUtc, x.ScheduledArrivalUtc, x.Status, x.Provider,
        x.ProviderFlightId, x.ProviderStatus, x.EstimatedDepartureUtc, x.ActualDepartureUtc,
        x.EstimatedArrivalUtc, x.ActualArrivalUtc, x.DepartureDelayMinutes,
        x.LastCheckedAt, x.CreatedAt, x.UpdatedAt);
}

public interface IFlightStatusProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<FlightProviderResult> GetFlightAsync(string identifier, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
}

public sealed record FlightProviderResult(
    string Outcome, string Provider, string Status, string? ProviderFlightId,
    string? ProviderStatus, DateTimeOffset? EstimatedDepartureUtc,
    DateTimeOffset? ActualDepartureUtc, DateTimeOffset? EstimatedArrivalUtc,
    DateTimeOffset? ActualArrivalUtc, int? DepartureDelayMinutes, string? Message = null);

public sealed class UnconfiguredFlightStatusProvider : IFlightStatusProvider
{
    public string Name => "FlightAware AeroAPI";
    public bool IsConfigured => false;
    public Task<FlightProviderResult> GetFlightAsync(string identifier, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        Task.FromResult(new FlightProviderResult("not_configured", Name, "unknown", null, null, null, null, null, null, null));
}

public sealed class FlightAwareAeroApiProvider(HttpClient client, IConfiguration configuration) : IFlightStatusProvider
{
    private readonly string? _apiKey = configuration["FLIGHTAWARE_API_KEY"];
    public string Name => "FlightAware AeroAPI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<FlightProviderResult> GetFlightAsync(
        string identifier, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        if (!IsConfigured)
            return new FlightProviderResult("not_configured", Name, "unknown", null, null, null, null, null, null, null);
        var path = "flights/" + Uri.EscapeDataString(identifier)
            + "?start=" + Uri.EscapeDataString(start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            + "&end=" + Uri.EscapeDataString(end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            + "&max_pages=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("x-apikey", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new FlightProviderResult("provider_error", Name, "unknown", null, null, null, null, null, null, null,
                $"FlightAware returned HTTP {(int)response.StatusCode}. Check API credentials, access and usage limits.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("flights", out var flights) || flights.ValueKind != JsonValueKind.Array || flights.GetArrayLength() == 0)
            return new FlightProviderResult("not_found", Name, "unknown", null, null, null, null, null, null, null);

        JsonElement? best = null;
        var bestDistance = double.MaxValue;
        foreach (var candidate in flights.EnumerateArray())
        {
            var scheduled = ReadDate(candidate, "scheduled_out");
            if (!scheduled.HasValue) continue;
            var distance = Math.Abs((scheduled.Value - start.AddHours(12)).TotalMinutes);
            if (distance < bestDistance) { bestDistance = distance; best = candidate; }
        }
        if (!best.HasValue || bestDistance > 24 * 60)
            return new FlightProviderResult("not_found", Name, "unknown", null, null, null, null, null, null, null);

        var item = best.Value;
        var rawStatus = ReadString(item, "status");
        var scheduledOut = ReadDate(item, "scheduled_out");
        var estimatedOut = ReadDate(item, "estimated_out");
        var actualOut = ReadDate(item, "actual_out") ?? ReadDate(item, "actual_off");
        var scheduledIn = ReadDate(item, "scheduled_in") ?? ReadDate(item, "scheduled_on");
        var estimatedIn = ReadDate(item, "estimated_in") ?? ReadDate(item, "estimated_on");
        var actualIn = ReadDate(item, "actual_in") ?? ReadDate(item, "actual_on");
        var departureReference = actualOut ?? estimatedOut;
        var delay = scheduledOut.HasValue && departureReference.HasValue
            ? (int?)Math.Round((departureReference.Value - scheduledOut.Value).TotalMinutes)
            : null;
        var status = NormalizeStatus(rawStatus);
        if (status == "scheduled" && delay >= 15) status = "delayed";
        return new FlightProviderResult(
            "found", Name, status, ReadString(item, "fa_flight_id"), rawStatus,
            estimatedOut, actualOut, estimatedIn, actualIn, delay);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        var value = ReadString(element, property);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime() : null;
    }

    private static string NormalizeStatus(string? raw)
    {
        var value = raw?.ToLowerInvariant() ?? "";
        if (value.Contains("cancel")) return "cancelled";
        if (value.Contains("divert")) return "diverted";
        if (value.Contains("land") || value.Contains("arriv")) return "landed";
        if (value.Contains("en route") || value.Contains("in flight") || value.Contains("airborne")) return "active";
        if (value.Contains("delay")) return "delayed";
        if (value.Contains("sched") || value.Contains("filed")) return "scheduled";
        return "unknown";
    }
}

public sealed record CreateTripFlightRequest(
    string FlightIdentifier, string OriginAirportCode, string DestinationAirportCode,
    DateTimeOffset ScheduledDepartureUtc, DateTimeOffset ScheduledArrivalUtc,
    string? JourneyGroup = null);

public sealed record TripFlightResponse(
    Guid Id, Guid TripId, string FlightIdentifier, string JourneyGroup, string OriginAirportCode,
    string DestinationAirportCode, DateTimeOffset ScheduledDepartureUtc,
    DateTimeOffset ScheduledArrivalUtc, string Status, string Provider,
    string? ProviderFlightId, string? ProviderStatus, DateTimeOffset? EstimatedDepartureUtc,
    DateTimeOffset? ActualDepartureUtc, DateTimeOffset? EstimatedArrivalUtc,
    DateTimeOffset? ActualArrivalUtc, int? DepartureDelayMinutes,
    DateTimeOffset? LastCheckedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record FlightRefreshResponse(
    TripFlightResponse Flight, bool HasDisruptionSignal, int DepartureDateActivityCount,
    int ArrivalDateActivityCount, string ItineraryImpactSummary);


public sealed record FlightConnectionsResponse(
    Guid TripId, DateTimeOffset AssessedAt,
    IReadOnlyList<FlightConnectionAssessment> Connections, string Disclaimer);

public sealed record FlightConnectionAssessment(
    Guid InboundFlightId, string JourneyGroup, string InboundFlightIdentifier,
    string InboundOriginAirportCode, string InboundDestinationAirportCode,
    DateTimeOffset InboundScheduledArrivalUtc, DateTimeOffset InboundEffectiveArrivalUtc,
    string InboundStatus, Guid OutboundFlightId, string OutboundFlightIdentifier,
    string OutboundOriginAirportCode, string OutboundDestinationAirportCode,
    DateTimeOffset OutboundScheduledDepartureUtc, DateTimeOffset OutboundEffectiveDepartureUtc,
    string OutboundStatus, int GapMinutes, bool SameAirport,
    string RiskLevel, int? HeuristicBufferMinutes, string TimeSource, string Explanation);
