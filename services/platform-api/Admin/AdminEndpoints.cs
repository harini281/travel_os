using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Domain;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin")
            .WithTags("Platform administration")
            .RequireAuthorization("PlatformAdmin");

        group.MapGet("/dashboard", GetDashboardAsync).WithName("GetAdminDashboard");
        group.MapGet("/users", GetUsersAsync).WithName("GetAdminUsers");
        group.MapGet("/trips", GetTripsAsync).WithName("GetAdminTrips");
        return endpoints;
    }

    private static async Task<IResult> GetDashboardAsync(
        TravelOsDbContext db,
        CancellationToken cancellationToken)
    {
        var users = await db.Users.CountAsync(cancellationToken);
        var trips = await db.Trips.CountAsync(cancellationToken);
        var places = await db.Activities
            .Where(activity => activity.Latitude != null && activity.Longitude != null)
            .Select(activity => activity.PlaceName)
            .Distinct()
            .CountAsync(cancellationToken);

        return Results.Ok(new AdminDashboardResponse(
            users,
            trips,
            places,
            await db.GeneratedItineraryDrafts.CountAsync(cancellationToken),
            DateTimeOffset.UtcNow));
    }

    private static async Task<IResult> GetUsersAsync(
        TravelOsDbContext db,
        UserManager<User> userManager,
        string? search,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 10_000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user =>
                user.Email!.Contains(term) || user.DisplayName.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var pageUsers = await query
            .OrderByDescending(user => user.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var users = new List<AdminUserListItem>(pageUsers.Count);
        foreach (var user in pageUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            var isLockedOut = await userManager.IsLockedOutAsync(user);
            users.Add(new AdminUserListItem(
                user.Id,
                user.Email ?? "",
                user.DisplayName,
                user.CreatedAt,
                user.EmailConfirmed,
                isLockedOut,
                roles.ToArray()));
        }

        return Results.Ok(new PagedAdminResponse<AdminUserListItem>(
            users, page, pageSize, total));
    }

    private static async Task<IResult> GetTripsAsync(
        TravelOsDbContext db,
        string? search,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 10_000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Trips.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(trip =>
                trip.Name.Contains(term) || trip.Destination.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var trips = await query
            .OrderByDescending(trip => trip.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(trip => new AdminTripListItem(
                trip.Id,
                trip.Name,
                trip.Destination,
                trip.StartDate,
                trip.EndDate,
                trip.OwnerUserId,
                trip.Members.Count,
                trip.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedAdminResponse<AdminTripListItem>(
            trips, page, pageSize, total));
    }
}

public sealed record AdminDashboardResponse(
    int RegisteredTravellers,
    int TripsCreated,
    int PlacesWithCoordinates,
    int AiDraftsCreated,
    DateTimeOffset GeneratedAt);

public sealed record AdminUserListItem(
    Guid Id,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt,
    bool EmailConfirmed,
    bool IsLockedOut,
    IReadOnlyCollection<string> Roles);

public sealed record AdminTripListItem(
    Guid Id,
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid OwnerUserId,
    int MemberCount,
    DateTimeOffset CreatedAt);

public sealed record PagedAdminResponse<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int Total);
