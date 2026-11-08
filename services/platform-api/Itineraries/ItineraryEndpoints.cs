using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Itineraries;

public static class ItineraryEndpoints
{
    public static IEndpointRouteBuilder MapItineraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/itinerary")
            .WithTags("Itineraries")
            .RequireAuthorization();

        group.MapGet("/", GetAsync).WithName("GetTripItinerary");
        group.MapPost("/", CreateAsync).WithName("CreateTripItinerary");
        group.MapPost("/days", AddDayAsync).WithName("AddItineraryDay");
        group.MapPost("/days/{dayId:guid}/activities", AddActivityAsync).WithName("AddItineraryActivity");
        group.MapPut("/activities/{activityId:guid}", UpdateActivityAsync).WithName("UpdateItineraryActivity");
        group.MapDelete("/activities/{activityId:guid}", DeleteActivityAsync).WithName("DeleteItineraryActivity");
        group.MapPost("/approve", ApproveAsync).WithName("ApproveItinerary");

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<Trip?> FindTripForMemberAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct)
    {
        return await db.Trips.AsNoTracking().FirstOrDefaultAsync(
            trip => trip.Id == tripId &&
                (trip.OwnerUserId == userId ||
                 db.TripMembers.Any(member => member.TripId == trip.Id && member.UserId == userId)),
            ct);
    }

    private static async Task<Trip?> FindTripForOwnerAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct)
    {
        return await db.Trips.FirstOrDefaultAsync(
            trip => trip.Id == tripId && trip.OwnerUserId == userId, ct);
    }

    private static ItineraryResponse ToResponse(Itinerary itinerary)
    {
        var days = itinerary.Days
            .OrderBy(day => day.DayNumber)
            .Select(day => new ItineraryDayResponse(
                day.Id,
                day.DayNumber,
                day.Date,
                day.Activities.OrderBy(activity => activity.SortOrder)
                    .Select(activity => new ActivityResponse(
                        activity.Id, activity.Title, activity.Description,
                        activity.PlaceName, activity.Latitude, activity.Longitude,
                        activity.DurationMinutes, activity.EstimatedCost,
                        activity.CurrencyCode, activity.SortOrder))
                    .ToArray()))
            .ToArray();

        return new ItineraryResponse(
            itinerary.Id, itinerary.TripId, itinerary.Version,
            itinerary.ApprovedAt is not null, itinerary.ApprovedAt, days);
    }

    private static IResult? ValidateActivity(ActivityRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 180)
            errors["title"] = ["Activity title is required and must be at most 180 characters."];

        if (request.Description?.Length > 2000)
            errors["description"] = ["Description must be at most 2,000 characters."];

        if (request.PlaceName?.Length > 200)
            errors["placeName"] = ["Place name must be at most 200 characters."];

        if (request.Latitude.HasValue != request.Longitude.HasValue)
            errors["coordinates"] = ["Latitude and longitude must be supplied together."];

        if (request.Latitude is < -90 or > 90)
            errors["latitude"] = ["Latitude must be between -90 and 90."];

        if (request.Longitude is < -180 or > 180)
            errors["longitude"] = ["Longitude must be between -180 and 180."];

        if (request.DurationMinutes is <= 0)
            errors["durationMinutes"] = ["Duration must be greater than zero."];

        if (request.EstimatedCost is < 0)
            errors["estimatedCost"] = ["Estimated cost cannot be negative."];

        if (request.CurrencyCode is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(request.CurrencyCode, "^[A-Za-z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static async Task<IResult> GetAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForMemberAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries.AsNoTracking()
            .Include(x => x.Days)
            .ThenInclude(x => x.Activities)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        return itinerary is null
            ? Results.NoContent()
            : Results.Ok(ToResponse(itinerary));
    }

    private static async Task<IResult> CreateAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var existing = await db.Itineraries.AnyAsync(x => x.TripId == tripId, ct);
        if (existing)
            return Results.Conflict(new { message = "An itinerary already exists for this trip." });

        var itinerary = new Itinerary { TripId = tripId, Version = 1 };
        db.Itineraries.Add(itinerary);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary",
            ToResponse(itinerary));
    }

    private static async Task<IResult> AddDayAsync(
        Guid tripId,
        AddItineraryDayRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        if (request.DayNumber < 1)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dayNumber"] = ["Day number must be at least 1."]
            });

        if (request.Date < trip.StartDate || request.Date > trip.EndDate)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["date"] = ["Day date must fall within the trip date range."]
            });

        var expectedDayNumber = request.Date.DayNumber - trip.StartDate.DayNumber;
        if (request.DayNumber != expectedDayNumber)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dayNumber"] = ["Day number must match the date's position in the trip."]
            });

        var duplicate = await db.ItineraryDays.AnyAsync(
            x => x.ItineraryId == itinerary.Id &&
                (x.DayNumber == request.DayNumber || x.Date == request.Date), ct);
        if (duplicate)
            return Results.Conflict(new { message = "A day already exists for this date." });

        var day = new ItineraryDay
        {
            ItineraryId = itinerary.Id,
            DayNumber = request.DayNumber,
            Date = request.Date
        };
        db.ItineraryDays.Add(day);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary/days/{day.Id}",
            new ItineraryDayResponse(day.Id, day.DayNumber, day.Date, []));
    }

    private static async Task<IResult> AddActivityAsync(
        Guid tripId,
        Guid dayId,
        ActivityRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateActivity(request);
        if (invalid is not null) return invalid;

        var day = await db.ItineraryDays
            .Include(x => x.Activities)
            .FirstOrDefaultAsync(x => x.Id == dayId, ct);
        if (day is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.Id == day.ItineraryId && x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        var activity = new Activity
        {
            ItineraryDayId = dayId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            PlaceName = request.PlaceName?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DurationMinutes = request.DurationMinutes,
            EstimatedCost = request.EstimatedCost,
            CurrencyCode = request.EstimatedCost.HasValue
                ? (request.CurrencyCode?.ToUpperInvariant() ?? trip.CurrencyCode)
                : request.CurrencyCode?.ToUpperInvariant(),
            SortOrder = request.SortOrder > 0
                ? request.SortOrder
                : day.Activities.Select(x => x.SortOrder).DefaultIfEmpty(0).Max() + 1
        };

        db.Activities.Add(activity);
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary/days/{dayId}/activities/{activity.Id}",
            new ActivityResponse(
                activity.Id, activity.Title, activity.Description, activity.PlaceName,
                activity.Latitude, activity.Longitude, activity.DurationMinutes,
                activity.EstimatedCost, activity.CurrencyCode, activity.SortOrder));
    }

    private static async Task<IResult> UpdateActivityAsync(
        Guid tripId,
        Guid activityId,
        ActivityRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateActivity(request);
        if (invalid is not null) return invalid;

        var activity = await db.Activities.FirstOrDefaultAsync(
            x => x.Id == activityId, ct);
        if (activity is null) return Results.NotFound();

        var day = await db.ItineraryDays.FirstOrDefaultAsync(
            x => x.Id == activity.ItineraryDayId, ct);
        if (day is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.Id == day.ItineraryId && x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        activity.Title = request.Title.Trim();
        activity.Description = request.Description?.Trim();
        activity.PlaceName = request.PlaceName?.Trim();
        activity.Latitude = request.Latitude;
        activity.Longitude = request.Longitude;
        activity.DurationMinutes = request.DurationMinutes;
        activity.EstimatedCost = request.EstimatedCost;
        activity.CurrencyCode = request.EstimatedCost.HasValue
            ? (request.CurrencyCode?.ToUpperInvariant() ?? trip.CurrencyCode)
            : request.CurrencyCode?.ToUpperInvariant();
        activity.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new ActivityResponse(
            activity.Id, activity.Title, activity.Description, activity.PlaceName,
            activity.Latitude, activity.Longitude, activity.DurationMinutes,
            activity.EstimatedCost, activity.CurrencyCode, activity.SortOrder));
    }

    private static async Task<IResult> DeleteActivityAsync(
        Guid tripId,
        Guid activityId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var activity = await db.Activities.FirstOrDefaultAsync(
            x => x.Id == activityId, ct);
        if (activity is null) return Results.NotFound();

        var day = await db.ItineraryDays.FirstOrDefaultAsync(
            x => x.Id == activity.ItineraryDayId, ct);
        if (day is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.Id == day.ItineraryId && x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        db.Activities.Remove(activity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ApproveAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries
            .Include(x => x.Days)
            .ThenInclude(x => x.Activities)
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "This itinerary is already approved." });

        if (itinerary.Days.Count == 0 || itinerary.Days.All(day => day.Activities.Count == 0))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["itinerary"] = ["Add at least one day and one activity before approving."]
            });

        itinerary.ApprovedByUserId = userId;
        itinerary.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(itinerary));
    }
}
