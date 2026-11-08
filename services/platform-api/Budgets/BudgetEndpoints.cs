using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Budgets;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/budget")
            .WithTags("Budgets")
            .RequireAuthorization();

        group.MapGet("/", GetAsync).WithName("GetTripBudget");
        group.MapPut("/", UpsertAsync).WithName("UpsertTripBudget");
        group.MapPost("/items", AddItemAsync).WithName("AddBudgetItem");
        group.MapPut("/items/{itemId:guid}", UpdateItemAsync).WithName("UpdateBudgetItem");
        group.MapDelete("/items/{itemId:guid}", DeleteItemAsync).WithName("DeleteBudgetItem");
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

    private static IResult? ValidateBudget(decimal amount, string? currencyCode)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount < 0)
            errors["amount"] = ["Budget amount cannot be negative."];
        if (string.IsNullOrWhiteSpace(currencyCode) ||
            !System.Text.RegularExpressions.Regex.IsMatch(currencyCode, "^[A-Za-z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static IResult? ValidateItem(BudgetItemRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 80)
            errors["category"] = ["Category is required and must be at most 80 characters."];
        if (request.Amount < 0)
            errors["amount"] = ["Planned amount cannot be negative."];
        if (request.CurrencyCode is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(request.CurrencyCode, "^[A-Za-z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static BudgetResponse ToResponse(Budget budget, IEnumerable<BudgetItem> items)
    {
        var itemList = items.OrderBy(x => x.Category)
            .Select(x => new BudgetItemResponse(
                x.Id, x.Category, x.Amount, x.CurrencyCode))
            .ToArray();

        var totals = itemList
            .GroupBy(x => x.CurrencyCode)
            .Select(group => new BudgetCurrencyTotal(
                group.Key, group.Sum(item => item.Amount)))
            .OrderBy(x => x.CurrencyCode)
            .ToArray();

        return new BudgetResponse(
            budget.Id, budget.TripId, budget.Amount, budget.CurrencyCode,
            itemList, totals);
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

        var budget = await db.Budgets.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        return budget is null
            ? Results.NoContent()
            : Results.Ok(ToResponse(budget, budget.Items));
    }

    private static async Task<IResult> UpsertAsync(
        Guid tripId,
        UpsertBudgetRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateBudget(request.Amount, request.CurrencyCode);
        if (invalid is not null) return invalid;

        var budget = await db.Budgets
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);

        if (budget is null)
        {
            budget = new Budget
            {
                TripId = tripId,
                Amount = request.Amount,
                CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant()
            };
            db.Budgets.Add(budget);
        }
        else
        {
            budget.Amount = request.Amount;
            budget.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(budget, budget.Items));
    }

    private static async Task<IResult> AddItemAsync(
        Guid tripId,
        BudgetItemRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateItem(request);
        if (invalid is not null) return invalid;

        var budget = await db.Budgets.FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        if (budget is null)
            return Results.Conflict(new { message = "Create the trip budget before adding budget items." });

        var item = new BudgetItem
        {
            BudgetId = budget.Id,
            Category = request.Category.Trim(),
            Amount = request.Amount,
            CurrencyCode = (request.CurrencyCode ?? budget.CurrencyCode).Trim().ToUpperInvariant()
        };
        db.BudgetItems.Add(item);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/trips/{tripId}/budget/items/{item.Id}",
            new BudgetItemResponse(item.Id, item.Category, item.Amount, item.CurrencyCode));
    }

    private static async Task<IResult> UpdateItemAsync(
        Guid tripId,
        Guid itemId,
        BudgetItemRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateItem(request);
        if (invalid is not null) return invalid;

        var budget = await db.Budgets.FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        if (budget is null) return Results.NotFound();

        var item = await db.BudgetItems.FirstOrDefaultAsync(
            x => x.Id == itemId && x.BudgetId == budget.Id, ct);
        if (item is null) return Results.NotFound();

        item.Category = request.Category.Trim();
        item.Amount = request.Amount;
        item.CurrencyCode = (request.CurrencyCode ?? budget.CurrencyCode).Trim().ToUpperInvariant();
        await db.SaveChangesAsync(ct);

        return Results.Ok(new BudgetItemResponse(
            item.Id, item.Category, item.Amount, item.CurrencyCode));
    }

    private static async Task<IResult> DeleteItemAsync(
        Guid tripId,
        Guid itemId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var budget = await db.Budgets.FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        if (budget is null) return Results.NotFound();

        var item = await db.BudgetItems.FirstOrDefaultAsync(
            x => x.Id == itemId && x.BudgetId == budget.Id, ct);
        if (item is null) return Results.NotFound();

        db.BudgetItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
