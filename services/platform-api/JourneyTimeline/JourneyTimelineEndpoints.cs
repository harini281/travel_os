using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.JourneyTimeline;

public static class JourneyTimelineEndpoints
{
    public static IEndpointRouteBuilder MapJourneyTimelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/trips/{tripId:guid}/journey-timeline", GetAsync)
            .RequireAuthorization()
            .WithTags("Journey timeline")
            .WithName("GetJourneyTimeline");
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        var claim = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var userId)) return Results.Unauthorized();
        var isMember = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct)
            || await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!isMember) return Results.NotFound();

        var flights = await db.TripFlights.AsNoTracking()
            .Where(x => x.TripId == tripId).ToListAsync(ct);
        var itinerary = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .Include(x => x.Days).ThenInclude(x => x.Activities)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        var items = new List<JourneyTimelineItem>();
        foreach (var flight in flights)
        {
            var departure = flight.ActualDepartureUtc ?? flight.EstimatedDepartureUtc ?? flight.ScheduledDepartureUtc;
            var arrival = flight.ActualArrivalUtc ?? flight.EstimatedArrivalUtc ?? flight.ScheduledArrivalUtc;
            items.Add(new JourneyTimelineItem(
                flight.Id, "flight", flight.FlightIdentifier, departure, departure.UtcDateTime.Date,
                flight.OriginAirportCode + " → " + flight.DestinationAirportCode,
                flight.Status, "Exact flight time; " + (flight.ActualDepartureUtc.HasValue || flight.EstimatedDepartureUtc.HasValue ? "provider-updated departure" : "scheduled departure"),
                flight.JourneyGroup, arrival));
        }

        if (itinerary is not null)
        {
            foreach (var day in itinerary.Days)
            foreach (var activity in day.Activities)
            {
                items.Add(new JourneyTimelineItem(
                    activity.Id, "activity", activity.Title, null,
                    day.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    activity.PlaceName, "date_only",
                    $"Day {day.DayNumber} · itinerary order {activity.SortOrder}" +
                    (activity.DurationMinutes.HasValue ? $" · {activity.DurationMinutes} min planned duration" : ""),
                    "Itinerary", null));
            }
        }

        var ordered = items.OrderBy(x => x.DateUtc).ThenBy(x => x.StartUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Type == "flight" ? 0 : 1).ToArray();

        var advisories = new List<JourneyTimelineAdvisory>();
        var orderedFlights = flights.OrderBy(x => x.ActualDepartureUtc ?? x.EstimatedDepartureUtc ?? x.ScheduledDepartureUtc).ToArray();
        for (var i = 0; i < orderedFlights.Length - 1; i++)
        {
            var first = orderedFlights[i];
            var second = orderedFlights[i + 1];
            var firstArrival = first.ActualArrivalUtc ?? first.EstimatedArrivalUtc ?? first.ScheduledArrivalUtc;
            var secondDeparture = second.ActualDepartureUtc ?? second.EstimatedDepartureUtc ?? second.ScheduledDepartureUtc;
            if (secondDeparture < firstArrival)
                advisories.Add(new JourneyTimelineAdvisory("flight_overlap", "high",
                    $"{first.FlightIdentifier} and {second.FlightIdentifier} have overlapping effective flight times. Verify the schedules and journey groups.",
                    new[] { first.Id, second.Id }));
        }

        if (itinerary is not null)
        {
            var activityDates = itinerary.Days.Where(d => d.Activities.Any()).Select(d => d.Date).ToHashSet();
            foreach (var flight in flights)
            {
                var departureDate = DateOnly.FromDateTime((flight.ActualDepartureUtc ?? flight.EstimatedDepartureUtc ?? flight.ScheduledDepartureUtc).UtcDateTime);
                var arrivalDate = DateOnly.FromDateTime((flight.ActualArrivalUtc ?? flight.EstimatedArrivalUtc ?? flight.ScheduledArrivalUtc).UtcDateTime);
                if (activityDates.Contains(departureDate) || activityDates.Contains(arrivalDate))
                    advisories.Add(new JourneyTimelineAdvisory("same_day_activity_review", "review",
                        $"Review itinerary activities on the same UTC calendar day as {flight.FlightIdentifier}. Activity start times and airport processing time are not recorded, so no precise conflict can be confirmed.",
                        new[] { flight.Id }));
            }
        }

        return Results.Ok(new JourneyTimelineResponse(
            tripId, DateTimeOffset.UtcNow, ordered, advisories,
            "Flight times use provider actual time, then provider estimate, then scheduled time. Itinerary activities are date/order-only because their start times are not currently stored. Same-day flags are review prompts, not confirmed conflicts. All dates are grouped using UTC."));
    }
}

public sealed record JourneyTimelineResponse(
    Guid TripId, DateTimeOffset GeneratedAt, IReadOnlyList<JourneyTimelineItem> Items,
    IReadOnlyList<JourneyTimelineAdvisory> Advisories, string Disclaimer);

public sealed record JourneyTimelineItem(
    Guid Id, string Type, string Title, DateTimeOffset? StartUtc, DateTime DateUtc,
    string? Location, string Status, string Detail, string Group, DateTimeOffset? EndUtc);

public sealed record JourneyTimelineAdvisory(
    string Code, string Severity, string Message, IReadOnlyList<Guid> RelatedItemIds);
