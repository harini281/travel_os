using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Preferences;

public static class PreferenceEndpoints
{
    private static readonly HashSet<string> AllowedPaces =
        new(StringComparer.OrdinalIgnoreCase) { "relaxed", "balanced", "fast" };
    private static readonly HashSet<string> AllowedStyles =
        new(StringComparer.OrdinalIgnoreCase) { "mixed", "culture", "nature", "adventure", "food", "relaxation" };
    private static readonly HashSet<string> AllowedInterests =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "culture", "history", "nature", "adventure", "food", "shopping",
            "photography", "nightlife", "beaches", "art", "wellness", "local experiences"
        };

    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/preferences")
            .WithTags("Traveller preferences")
            .RequireAuthorization();

        group.MapGet("/me", GetMineAsync).WithName("GetMyTripPreferences");
        group.MapPut("/me", SaveMineAsync).WithName("SaveMyTripPreferences");
        group.MapGet("/group", GetGroupAsync).WithName("GetGroupTripPreferences");
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
        await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static string[] ParseInterests(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static MyTripPreferenceResponse ToMine(TripMemberPreference preference) =>
        new(preference.Id, preference.TripId, preference.UserId,
            ParseInterests(preference.InterestsJson), preference.Pace,
            preference.TravelStyle, preference.DailyBudgetAmount,
            preference.DailyBudgetCurrencyCode, preference.Notes, preference.UpdatedAt);

    private static async Task<IResult> GetMineAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var preference = await db.TripMemberPreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        return preference is null ? Results.NoContent() : Results.Ok(ToMine(preference));
    }

    private static async Task<IResult> SaveMineAsync(
        Guid tripId, SaveTripPreferenceRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        var interests = (request.Interests ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (interests.Length > 8 || interests.Any(x => !AllowedInterests.Contains(x)))
            errors["interests"] = ["Choose up to 8 interests from the supported list."];
        if (!AllowedPaces.Contains(request.Pace ?? ""))
            errors["pace"] = ["Choose relaxed, balanced or fast."];
        if (!AllowedStyles.Contains(request.TravelStyle ?? ""))
            errors["travelStyle"] = ["Choose a supported travel style."];
        if (request.DailyBudgetAmount is < 0)
            errors["dailyBudgetAmount"] = ["Daily budget cannot be negative."];
        if (request.DailyBudgetAmount.HasValue &&
            (string.IsNullOrWhiteSpace(request.DailyBudgetCurrencyCode) ||
             !System.Text.RegularExpressions.Regex.IsMatch(request.DailyBudgetCurrencyCode, "^[A-Za-z]{3}$")))
            errors["dailyBudgetCurrencyCode"] = ["Provide a three-letter currency code when a daily budget is set."];
        if (request.Notes?.Length > 500)
            errors["notes"] = ["Notes must be 500 characters or fewer."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var preference = await db.TripMemberPreferences
            .FirstOrDefaultAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (preference is null)
        {
            preference = new TripMemberPreference { TripId = tripId, UserId = userId };
            db.TripMemberPreferences.Add(preference);
        }

        preference.InterestsJson = JsonSerializer.Serialize(interests);
        preference.Pace = request.Pace.ToLowerInvariant();
        preference.TravelStyle = request.TravelStyle.ToLowerInvariant();
        preference.DailyBudgetAmount = request.DailyBudgetAmount;
        preference.DailyBudgetCurrencyCode = request.DailyBudgetAmount.HasValue
            ? request.DailyBudgetCurrencyCode!.Trim().ToUpperInvariant()
            : null;
        preference.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        preference.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToMine(preference));
    }

    private static async Task<IResult> GetGroupAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var preferences = await (
            from preference in db.TripMemberPreferences.AsNoTracking()
            join member in db.TripMembers.AsNoTracking()
                on new { preference.TripId, preference.UserId } equals new { member.TripId, member.UserId }
            join user in db.Users.AsNoTracking() on preference.UserId equals user.Id
            where preference.TripId == tripId
            orderby user.DisplayName
            select new
            {
                preference.UserId, user.DisplayName, preference.InterestsJson,
                preference.Pace, preference.TravelStyle, preference.DailyBudgetAmount,
                preference.DailyBudgetCurrencyCode, preference.UpdatedAt
            }
        ).ToListAsync(ct);

        return Results.Ok(preferences.Select(x => new GroupTripPreferenceResponse(
            x.UserId, x.DisplayName, ParseInterests(x.InterestsJson),
            x.Pace, x.TravelStyle, x.DailyBudgetAmount,
            x.DailyBudgetCurrencyCode, x.UpdatedAt)).ToArray());
    }
}
