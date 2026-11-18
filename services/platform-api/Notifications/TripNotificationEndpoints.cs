using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Notifications;

public static class TripNotificationEndpoints
{
    private static readonly string[] AllowedTypes =
        ["reminder", "flight", "activity", "schedule", "trip", "system"];

    public static IEndpointRouteBuilder MapTripNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("ListMyNotifications");
        group.MapPost("/", CreateAsync).WithName("CreateTripNotification");
        group.MapPost("/{notificationId:guid}/read", MarkReadAsync).WithName("MarkNotificationRead");
        group.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead");
        group.MapDelete("/{notificationId:guid}", DismissAsync).WithName("DismissNotification");
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
        await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct)
        || await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct,
        bool unreadOnly = false, int limit = 50)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (limit is < 1 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["limit"] = ["Limit must be between 1 and 100."] });

        var now = DateTimeOffset.UtcNow;
        var query = db.TripNotifications.AsNoTracking()
            .Where(x => x.UserId == userId && x.DismissedAt == null
                && (x.ScheduledForUtc == null || x.ScheduledForUtc <= now));
        if (unreadOnly) query = query.Where(x => x.ReadAt == null);

        var items = await query.OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);
        var unreadCount = await db.TripNotifications.CountAsync(x =>
            x.UserId == userId && x.ReadAt == null && x.DismissedAt == null
            && (x.ScheduledForUtc == null || x.ScheduledForUtc <= now), ct);
        return Results.Ok(new { items, unreadCount });
    }

    private static async Task<IResult> CreateAsync(
        CreateTripNotificationRequest request, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(request.TripId, userId, db, ct)) return Results.NotFound();

        var type = request.Type?.Trim().ToLowerInvariant() ?? "";
        var title = request.Title?.Trim() ?? "";
        var message = request.Message?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (!AllowedTypes.Contains(type))
            errors["type"] = ["Choose a supported notification type."];
        if (title.Length is < 1 or > 120)
            errors["title"] = ["Title is required and must be at most 120 characters."];
        if (message.Length is < 1 or > 500)
            errors["message"] = ["Message is required and must be at most 500 characters."];
        if (request.ScheduledForUtc.HasValue &&
            request.ScheduledForUtc.Value < DateTimeOffset.UtcNow.AddMinutes(-1))
            errors["scheduledForUtc"] = ["Scheduled reminder time must be in the future."];
        if (request.DedupeKey?.Length > 240)
            errors["dedupeKey"] = ["Dedupe key must be at most 240 characters."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var notification = new TripNotification
        {
            TripId = request.TripId,
            UserId = userId,
            CreatedByUserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ScheduledForUtc = request.ScheduledForUtc?.ToUniversalTime(),
            DedupeKey = string.IsNullOrWhiteSpace(request.DedupeKey) ? null : request.DedupeKey.Trim()
        };
        db.TripNotifications.Add(notification);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (notification.DedupeKey is not null)
        {
            return Results.Conflict(new
            {
                code = "duplicate_notification",
                message = "A notification with this deduplication key already exists."
            });
        }

        return Results.Created($"/api/v1/notifications/{notification.Id}", ToResponse(notification));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid notificationId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var notification = await db.TripNotifications.FirstOrDefaultAsync(
            x => x.Id == notificationId && x.UserId == userId && x.DismissedAt == null, ct);
        if (notification is null) return Results.NotFound();
        notification.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(notification));
    }

    private static async Task<IResult> MarkAllReadAsync(
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var now = DateTimeOffset.UtcNow;
        var unread = await db.TripNotifications.Where(x =>
            x.UserId == userId && x.ReadAt == null && x.DismissedAt == null
            && (x.ScheduledForUtc == null || x.ScheduledForUtc <= now)).ToListAsync(ct);
        foreach (var notification in unread) notification.ReadAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { updatedCount = unread.Count });
    }

    private static async Task<IResult> DismissAsync(
        Guid notificationId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var notification = await db.TripNotifications.FirstOrDefaultAsync(
            x => x.Id == notificationId && x.UserId == userId, ct);
        if (notification is null) return Results.NotFound();
        notification.DismissedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static TripNotificationResponse ToResponse(TripNotification x) =>
        new(x.Id, x.TripId, x.Type, x.Title, x.Message, x.ScheduledForUtc,
            x.CreatedAt, x.ReadAt, x.DismissedAt);

    public sealed record CreateTripNotificationRequest(
        Guid TripId, string Type, string Title, string Message,
        DateTimeOffset? ScheduledForUtc = null, string? DedupeKey = null);

    public sealed record TripNotificationResponse(
        Guid Id, Guid TripId, string Type, string Title, string Message,
        DateTimeOffset? ScheduledForUtc, DateTimeOffset CreatedAt,
        DateTimeOffset? ReadAt, DateTimeOffset? DismissedAt);
}
