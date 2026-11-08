using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Expenses;

public static class ExpenseEndpoints
{
    private static readonly HashSet<string> Categories =
        new(StringComparer.OrdinalIgnoreCase)
        { "accommodation", "transport", "food", "activities", "shopping", "other" };

    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/expenses")
            .WithTags("Shared expenses")
            .RequireAuthorization();
        group.MapGet("/", ListAsync).WithName("ListTripExpenses");
        group.MapPost("/", CreateAsync).WithName("CreateTripExpense");
        group.MapDelete("/{expenseId:guid}", DeleteAsync).WithName("DeleteTripExpense");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static Task<bool> IsMemberAsync(Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var expenses = await db.TripExpenses.AsNoTracking()
            .Include(x => x.Shares)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        var userIds = expenses.SelectMany(x => x.Shares.Select(s => s.UserId))
            .Concat(expenses.Select(x => x.PaidByUserId))
            .Where(x => x != Guid.Empty).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var response = expenses.Select(expense => new TripExpenseResponse(
            expense.Id, expense.TripId, expense.Title, expense.Category, expense.Notes,
            expense.Amount, expense.CurrencyCode, expense.ExpenseDate,
            expense.PaidByUserId, names.GetValueOrDefault(expense.PaidByUserId, "Traveller"),
            expense.CreatedByUserId, expense.CreatedAt,
            expense.Shares.OrderBy(x => names.GetValueOrDefault(x.UserId, "Traveller"))
                .Select(share => new TripExpenseShareResponse(
                    share.UserId, names.GetValueOrDefault(share.UserId, "Traveller"), share.ShareAmount))
                .ToArray()
        )).ToArray();

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateAsync(
        Guid tripId, CreateTripExpenseRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var title = request.Title?.Trim() ?? "";
        var category = request.Category?.Trim().ToLowerInvariant() ?? "";
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
        var participants = (request.ParticipantUserIds ?? []).Distinct().ToArray();
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 2 or > 160) errors["title"] = ["Expense title must be between 2 and 160 characters."];
        if (!Categories.Contains(category)) errors["category"] = ["Choose a supported expense category."];
        if (request.Notes?.Length > 500) errors["notes"] = ["Notes must be 500 characters or fewer."];
        if (request.Amount <= 0 || request.Amount > 999999999999999.9999m)
            errors["amount"] = ["Amount must be greater than zero and within the supported limit."];
        if (!System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];
        if (participants.Length is < 1 or > 30)
            errors["participantUserIds"] = ["Choose between 1 and 30 trip members to split this expense."];
        if (!participants.Contains(request.PaidByUserId))
            errors["paidByUserId"] = ["The person who paid must be included in the split."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var validMembers = await db.TripMembers.AsNoTracking()
            .Where(x => x.TripId == tripId && participants.Contains(x.UserId))
            .Select(x => x.UserId).ToListAsync(ct);
        if (validMembers.Count != participants.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["participantUserIds"] = ["Every participant must be a current member of this trip."]
            });

        var roundedAmount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);
        if (roundedAmount <= 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["amount"] = ["Amount is too small for the supported precision."]
            });

        var expense = new TripExpense
        {
            TripId = tripId, PaidByUserId = request.PaidByUserId,
            CreatedByUserId = userId, Title = title, Category = category,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Amount = roundedAmount,
            CurrencyCode = currency, ExpenseDate = request.ExpenseDate
        };

        var orderedParticipants = participants.OrderBy(x => x).ToArray();
        var baseShare = decimal.Truncate((expense.Amount / orderedParticipants.Length) * 10000m) / 10000m;
        var remainder = expense.Amount - (baseShare * orderedParticipants.Length);
        for (var i = 0; i < orderedParticipants.Length; i++)
        {
            expense.Shares.Add(new TripExpenseShare
            {
                ExpenseId = expense.Id,
                UserId = orderedParticipants[i],
                ShareAmount = baseShare + (i == 0 ? remainder : 0m)
            });
        }

        db.TripExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/trips/{tripId}/expenses/{expense.Id}",
            new { expense.Id, expense.Amount, expense.CurrencyCode, participantCount = expense.Shares.Count });
    }

    private static async Task<IResult> DeleteAsync(
        Guid tripId, Guid expenseId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var expense = await db.TripExpenses.FirstOrDefaultAsync(
            x => x.Id == expenseId && x.TripId == tripId, ct);
        if (expense is null) return Results.NotFound();
        var isOwner = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
        if (expense.CreatedByUserId != userId && !isOwner) return Results.Forbid();

        db.TripExpenses.Remove(expense);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
