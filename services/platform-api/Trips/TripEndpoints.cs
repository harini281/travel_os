using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Trips;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips")
            .WithTags("Trips")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync).WithName("CreateTrip");
        group.MapGet("/", ListMineAsync).WithName("ListMyTrips");
        group.MapGet("/{tripId:guid}", GetByIdAsync).WithName("GetTrip");
        group.MapPut("/{tripId:guid}", UpdateAsync).WithName("UpdateTrip");
        group.MapDelete("/{tripId:guid}", DeleteAsync).WithName("DeleteTrip");

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static IResult? ValidateTrip(
        string? name, string? destination,
        DateOnly startDate, DateOnly endDate, string? currencyCode)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            errors["name"] = ["Trip name is required and must be at most 160 characters."];

        if (string.IsNullOrWhiteSpace(destination) || destination.Trim().Length > 160)
            errors["destination"] = ["Destination is required and must be at most 160 characters."];

        if (endDate < startDate)
            errors["endDate"] = ["End date must be on or after the start date."];

        if (string.IsNullOrWhiteSpace(currencyCode) ||
            !System.Text.RegularExpressions.Regex.IsMatch(currencyCode, "^[A-Za-z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters (for example, USD or LKR)."];

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static TripResponse ToResponse(Trip trip, Guid currentUserId, int memberCount)
    {
        var role = trip.OwnerUserId == currentUserId
            ? "owner"
            : "member";
        return new TripResponse(
            trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.EndDate,
            trip.CurrencyCode, trip.OwnerUserId, role, memberCount, trip.CreatedAt);
    }

    private static async Task<IResult> CreateAsync(
        CreateTripRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var invalid = ValidateTrip(
            request.Name, request.Destination,
            request.StartDate, request.EndDate, request.CurrencyCode);
        if (invalid is not null) return invalid;

        var userExists = await db.Users.AnyAsync(x => x.Id == userId, cancellationToken);
        if (!userExists) return Results.Unauthorized();

        var trip = new Trip
        {
            OwnerUserId = userId,
            Name = request.Name.Trim(),
            Destination = request.Destination.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant()
        };

        trip.Members.Add(new TripMember
        {
            TripId = trip.Id,
            UserId = userId,
            Role = "owner"
        });

        db.Trips.Add(trip);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/v1/trips/{trip.Id}",
            ToResponse(trip, userId, 1));
    }

    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var trips = await db.Trips.AsNoTracking()
            .Where(trip => trip.OwnerUserId == userId ||
                db.TripMembers.Any(member => member.TripId == trip.Id && member.UserId == userId))
            .OrderBy(trip => trip.StartDate)
            .ToListAsync(cancellationToken);

        var tripIds = trips.Select(x => x.Id).ToArray();
        var counts = await db.TripMembers.AsNoTracking()
            .Where(member => tripIds.Contains(member.TripId))
            .GroupBy(member => member.TripId)
            .Select(group => new { TripId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

        return Results.Ok(trips.Select(trip =>
            ToResponse(trip, userId, counts.GetValueOrDefault(trip.Id))).ToArray());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var trip = await db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tripId, cancellationToken);
        if (trip is null) return Results.NotFound();

        var isMember = trip.OwnerUserId == userId ||
            await db.TripMembers.AnyAsync(
                x => x.TripId == tripId && x.UserId == userId, cancellationToken);
        if (!isMember) return Results.NotFound();

        var count = await db.TripMembers.CountAsync(
            x => x.TripId == tripId, cancellationToken);
        return Results.Ok(ToResponse(trip, userId, count));
    }

    private static async Task<IResult> UpdateAsync(
        Guid tripId,
        UpdateTripRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var invalid = ValidateTrip(
            request.Name, request.Destination,
            request.StartDate, request.EndDate, request.CurrencyCode);
        if (invalid is not null) return invalid;

        var trip = await db.Trips.FirstOrDefaultAsync(
            x => x.Id == tripId, cancellationToken);
        if (trip is null) return Results.NotFound();
        if (trip.OwnerUserId != userId) return Results.Forbid();

        trip.Name = request.Name.Trim();
        trip.Destination = request.Destination.Trim();
        trip.StartDate = request.StartDate;
        trip.EndDate = request.EndDate;
        trip.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.TripMembers.CountAsync(
            x => x.TripId == tripId, cancellationToken);
        return Results.Ok(ToResponse(trip, userId, count));
    }

    private static async Task<IResult> DeleteAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();

        var trip = await db.Trips.FirstOrDefaultAsync(
            x => x.Id == tripId, cancellationToken);
        if (trip is null) return Results.NotFound();
        if (trip.OwnerUserId != userId) return Results.Forbid();

        db.Trips.Remove(trip);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
