using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.BudgetIntelligence;

public static class BudgetIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapBudgetIntelligenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/trips/{tripId:guid}/budget-intelligence", GetAsync)
            .WithTags("Budget intelligence").RequireAuthorization();
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<IResult> GetAsync(
        Guid tripId, string? reportingCurrencyCode, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var isMember = await db.TripMembers.AnyAsync(
            x => x.TripId == tripId && x.UserId == userId, ct);
        if (!isMember) return Results.NotFound();

        var target = reportingCurrencyCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(target))
            target = await db.Budgets.AsNoTracking()
                .Where(x => x.TripId == tripId)
                .Select(x => x.CurrencyCode)
                .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(target))
            target = await db.Trips.AsNoTracking()
                .Where(x => x.Id == tripId)
                .Select(x => x.CurrencyCode)
                .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(target) ||
            !System.Text.RegularExpressions.Regex.IsMatch(target, "^[A-Z]{3}$"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reportingCurrencyCode"] = ["Use a three-letter ISO 4217 currency code."]
            });

        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tripId, ct);
        if (trip is null) return Results.NotFound();

        var expenses = await db.TripExpenses.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderBy(x => x.ExpenseDate).ThenBy(x => x.CreatedAt)
            .Select(x => new ExpenseSource(x.Id, x.Title, x.Category, x.Amount,
                x.CurrencyCode, x.ExpenseDate))
            .ToListAsync(ct);

        var snapshots = await db.TripExpenseConversions.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ConvertedCurrencyCode == target)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ConversionSource(x.Id, x.ExpenseId, x.OriginalAmount,
                x.OriginalCurrencyCode, x.ConvertedAmount, x.ConvertedCurrencyCode,
                x.ExchangeRate, x.ExpenseDate, x.RateDate, x.Provider, x.CreatedAt))
            .ToListAsync(ct);

        var latestByExpense = snapshots
            .GroupBy(x => x.ExpenseId)
            .ToDictionary(x => x.Key, x => x.First());

        var converted = new List<BudgetExpenseLine>();
        var unconverted = new List<UnconvertedExpenseLine>();
        foreach (var expense in expenses)
        {
            if (expense.CurrencyCode == target)
            {
                converted.Add(new BudgetExpenseLine(expense.Id, expense.Title,
                    expense.Category, expense.Amount, expense.CurrencyCode,
                    expense.Amount, target, 1m, expense.ExpenseDate,
                    "Original currency", true));
                continue;
            }

            if (latestByExpense.TryGetValue(expense.Id, out var snapshot) &&
                snapshot.OriginalAmount == expense.Amount &&
                snapshot.OriginalCurrencyCode == expense.CurrencyCode &&
                snapshot.ExpenseDate == expense.ExpenseDate &&
                snapshot.RateDate <= expense.ExpenseDate &&
                snapshot.ExchangeRate > 0m)
            {
                converted.Add(new BudgetExpenseLine(expense.Id, expense.Title,
                    expense.Category, expense.Amount, expense.CurrencyCode,
                    snapshot.ConvertedAmount, target, snapshot.ExchangeRate,
                    snapshot.RateDate, snapshot.Provider, true));
            }
            else
            {
                unconverted.Add(new UnconvertedExpenseLine(
                    expense.Id, expense.Title, expense.Category,
                    expense.Amount, expense.CurrencyCode, expense.ExpenseDate,
                    latestByExpense.ContainsKey(expense.Id)
                        ? "The saved conversion no longer matches the current expense. Convert it again."
                        : "No saved conversion exists for the selected reporting currency."));
            }
        }

        var budget = await db.Budgets.AsNoTracking().Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        var budgetComparable = budget is not null && budget.CurrencyCode == target;
        decimal? budgetAmount = budgetComparable ? budget!.Amount : null;
        var convertedTotal = converted.Sum(x => x.ConvertedAmount);
        decimal? remaining = budgetAmount.HasValue ? budgetAmount.Value - convertedTotal : null;
        var elapsedDays = Math.Max(0, Math.Min(
            DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - trip.StartDate.DayNumber + 1,
            trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1));
        var totalDays = Math.Max(1, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1);
        var dailySpend = elapsedDays > 0 && converted.Count > 0
            ? convertedTotal / elapsedDays : (decimal?)null;
        var projected = dailySpend.HasValue ? dailySpend.Value * totalDays : (decimal?)null;
        var status = budget is null ? "no_budget"
            : !budgetComparable ? "budget_currency_mismatch"
            : unconverted.Count > 0 ? "partial_data"
            : convertedTotal > budget!.Amount ? "over_budget"
            : projected.HasValue && projected.Value > budget.Amount ? "projected_over_budget"
            : "on_track";

        var plannedInReportingCurrency = budget?.Items
            .Where(x => x.CurrencyCode == target)
            .GroupBy(x => x.Category.Trim().ToLowerInvariant())
            .OrderBy(x => x.Key)
            .Select(x => new PlannedBudgetCategory(x.Key, x.Sum(i => i.Amount)))
            .ToArray() ?? [];

        var unconvertedByCurrency = unconverted
            .GroupBy(x => x.CurrencyCode)
            .OrderBy(x => x.Key)
            .Select(x => new UnconvertedCurrencyTotal(
                x.Key, x.Sum(item => item.OriginalAmount), x.Count()))
            .ToArray();

        var categories = converted
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "other" : x.Category.Trim().ToLowerInvariant())
            .OrderByDescending(x => x.Sum(item => item.ConvertedAmount))
            .Select(x => new ConvertedCategoryTotal(
                x.Key, x.Sum(item => item.ConvertedAmount), x.Count(),
                plannedInReportingCurrency.FirstOrDefault(p => p.Category == x.Key)?.PlannedAmount ?? 0m))
            .ToArray();

        var explanation = budget is null
            ? "Create a trip budget to compare converted spending against a budget cap."
            : !budgetComparable
                ? $"The budget is in {budget!.CurrencyCode}, while this report is in {target}. Change the reporting currency to {budget.CurrencyCode} to compare them without an unrecorded conversion."
                : unconverted.Count > 0
                    ? $"{unconverted.Count} expenses are excluded because no matching saved conversion exists. The total is partial and must not be treated as complete."
                    : "All recorded expenses have a valid saved conversion or already use the reporting currency.";

        return Results.Ok(new BudgetIntelligenceResponse(
            tripId, DateTimeOffset.UtcNow, target, budget?.CurrencyCode,
            budgetAmount, budgetComparable, convertedTotal,
            budgetAmount.HasValue ? remaining : null,
            dailySpend, projected, status, explanation,
            converted.ToArray(), unconverted.ToArray(), unconvertedByCurrency,
            categories, plannedInReportingCurrency, expenses.Count,
            converted.Count, unconverted.Count, elapsedDays,
            Math.Max(0, totalDays - elapsedDays)));
    }

    private sealed record ExpenseSource(Guid Id, string Title, string Category,
        decimal Amount, string CurrencyCode, DateOnly ExpenseDate);
    private sealed record ConversionSource(Guid Id, Guid ExpenseId,
        decimal OriginalAmount, string OriginalCurrencyCode, decimal ConvertedAmount,
        string ConvertedCurrencyCode, decimal ExchangeRate, DateOnly ExpenseDate,
        DateOnly RateDate, string Provider, DateTimeOffset CreatedAt);
}

public sealed record BudgetIntelligenceResponse(
    Guid TripId, DateTimeOffset GeneratedAt, string ReportingCurrencyCode,
    string? BudgetCurrencyCode, decimal? BudgetAmount, bool BudgetComparable,
    decimal ConvertedActualTotal, decimal? RemainingBudget,
    decimal? AverageDailySpend, decimal? ProjectedTotal, string Status,
    string Explanation, BudgetExpenseLine[] ConvertedExpenses,
    UnconvertedExpenseLine[] UnconvertedExpenses,
    UnconvertedCurrencyTotal[] UnconvertedByCurrency,
    ConvertedCategoryTotal[] CategoryTotals,
    PlannedBudgetCategory[] PlannedBudgetCategories,
    int ExpenseCount, int ConvertedExpenseCount, int UnconvertedExpenseCount,
    int ElapsedDays, int RemainingDays);

public sealed record BudgetExpenseLine(
    Guid ExpenseId, string Title, string Category, decimal OriginalAmount,
    string OriginalCurrencyCode, decimal ConvertedAmount,
    string ReportingCurrencyCode, decimal ExchangeRate,
    DateOnly RateDate, string Provider, bool IsConverted);

public sealed record UnconvertedExpenseLine(
    Guid ExpenseId, string Title, string Category, decimal OriginalAmount,
    string CurrencyCode, DateOnly ExpenseDate, string Reason);

public sealed record UnconvertedCurrencyTotal(
    string CurrencyCode, decimal OriginalAmount, int ExpenseCount);

public sealed record ConvertedCategoryTotal(
    string Category, decimal ActualAmount, int ExpenseCount, decimal PlannedAmount);

public sealed record PlannedBudgetCategory(string Category, decimal PlannedAmount);
