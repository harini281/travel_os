using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.ExpenseIntelligence;

public static class ExpenseIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapExpenseIntelligenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/trips/{tripId:guid}/expense-intelligence", GetAsync)
            .WithTags("Expense intelligence")
            .RequireAuthorization();
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<IResult> GetAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var isMember = await db.TripMembers.AnyAsync(
            x => x.TripId == tripId && x.UserId == userId, ct);
        var trip = isMember
            ? await db.Trips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tripId, ct)
            : null;
        if (trip is null) return Results.NotFound();

        var expenses = await db.TripExpenses.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderBy(x => x.ExpenseDate).ThenBy(x => x.CreatedAt)
            .Select(x => new ExpenseRow(x.Id, x.Title, x.Category, x.Amount,
                x.CurrencyCode, x.ExpenseDate, x.PaidByUserId))
            .ToListAsync(ct);

        var receipts = await db.TripReceipts.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .Select(x => new ReceiptRow(x.Id, x.ExpenseId, x.MerchantName,
                x.ReceiptDate, x.TotalAmount, x.CurrencyCode, x.ExtractionStatus,
                x.OriginalFileName))
            .ToListAsync(ct);

        var budget = await db.Budgets.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);

        var currencies = expenses.GroupBy(x => x.CurrencyCode)
            .OrderBy(x => x.Key)
            .Select(group => new CurrencyActuals(
                group.Key,
                group.Sum(x => x.Amount),
                group.Count(),
                group.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "other" : x.Category)
                    .OrderByDescending(category => category.Sum(x => x.Amount))
                    .Select(category => new CategoryActual(category.Key,
                        category.Sum(x => x.Amount), category.Count()))
                    .ToArray()))
            .ToArray();

        var receiptMismatches = receipts
            .Where(receipt => receipt.ExpenseId.HasValue && receipt.TotalAmount.HasValue)
            .Select(receipt => new
            {
                Receipt = receipt,
                Expense = expenses.FirstOrDefault(expense => expense.Id == receipt.ExpenseId)
            })
            .Where(pair => pair.Expense is not null &&
                (pair.Receipt.TotalAmount != pair.Expense.Amount ||
                 (!string.IsNullOrWhiteSpace(pair.Receipt.CurrencyCode) &&
                  pair.Receipt.CurrencyCode != pair.Expense.CurrencyCode)))
            .Select(pair => new ReceiptMismatch(
                pair.Receipt.Id, pair.Expense!.Id, pair.Receipt.FileName,
                pair.Receipt.TotalAmount!.Value, pair.Receipt.CurrencyCode,
                pair.Expense.Amount, pair.Expense.CurrencyCode,
                pair.Receipt.Status == "confirmed" ? "confirmed_receipt_mismatch" : "unconfirmed_receipt_suggestion",
                pair.Receipt.Status == "confirmed"
                    ? "The confirmed receipt amount or currency differs from the linked expense."
                    : "The receipt contains an unconfirmed amount that differs from the linked expense. Review it before relying on this comparison."))
            .ToArray();

        var duplicateCandidates = expenses
            .GroupBy(x => new
            {
                x.ExpenseDate, x.Amount, x.CurrencyCode,
                Title = NormalizeTitle(x.Title)
            })
            .Where(group => group.Count() > 1 && group.Key.Title.Length >= 3)
            .Select(group => new DuplicateExpenseCandidate(
                group.Key.ExpenseDate, group.Key.Amount, group.Key.CurrencyCode,
                group.Select(x => new DuplicateExpenseItem(x.Id, x.Title, x.Category)).ToArray(),
                "These entries share a date, amount, currency and normalized title. They may be legitimate separate purchases; review before taking action."))
            .ToArray();

        var budgetCurrency = budget?.CurrencyCode;
        var actualInBudgetCurrency = budget is null
            ? null
            : expenses.Where(x => x.CurrencyCode == budget.CurrencyCode).Sum(x => x.Amount);
        var elapsedDays = Math.Max(0, Math.Min(
            (DateOnly.FromDateTime(DateTime.UtcNow) - trip.StartDate).Days + 1,
            (trip.EndDate - trip.StartDate).Days + 1));
        var totalDays = Math.Max(1, (trip.EndDate - trip.StartDate).Days + 1);
        var remainingDays = Math.Max(0, totalDays - elapsedDays);
        decimal? dailyBurn = elapsedDays > 0 && actualInBudgetCurrency.HasValue
            ? actualInBudgetCurrency.Value / elapsedDays : null;
        decimal? projectedTotal = dailyBurn.HasValue
            ? dailyBurn.Value * totalDays : null;
        decimal? projectedVariance = projectedTotal.HasValue && budget is not null
            ? budget.Amount - projectedTotal.Value : null;

        var forecast = budget is null
            ? new BudgetForecast(null, null, null, null, null, null, "no_budget", "Set a trip budget to enable a forecast.")
            : new BudgetForecast(
                budget.Amount, budget.CurrencyCode, actualInBudgetCurrency,
                budget.Amount - actualInBudgetCurrency!.Value,
                dailyBurn, projectedTotal,
                actualInBudgetCurrency > budget.Amount ? "over_budget" :
                projectedTotal.HasValue && projectedTotal > budget.Amount ? "projected_over_budget" : "on_track",
                actualInBudgetCurrency > budget.Amount
                    ? "Recorded expenses in the budget currency have exceeded the trip budget."
                    : projectedTotal.HasValue && projectedTotal > budget.Amount
                        ? "At the current average daily spend, the trip may exceed its budget."
                        : "Based only on recorded expenses in the budget currency. Other currencies are not converted.",
                projectedVariance, elapsedDays, remainingDays);

        var plannedCategories = budget?.Items
            .GroupBy(x => new { x.CurrencyCode, Category = x.Category.Trim().ToLowerInvariant() })
            .OrderBy(group => group.Key.CurrencyCode).ThenBy(group => group.Key.Category)
            .Select(group => new PlannedCategoryTotal(
                group.Key.CurrencyCode, group.Key.Category, group.Sum(x => x.Amount)))
            .ToArray() ?? [];

        var categoryKeys = expenses.Select(x => (x.CurrencyCode, Category: x.Category.Trim().ToLowerInvariant()))
            .Concat(plannedCategories.Select(x => (x.CurrencyCode, x.Category)))
            .Distinct().ToArray();
        var categoryComparisons = categoryKeys.Select(key =>
            new CategoryComparison(key.CurrencyCode, key.Category,
                plannedCategories.FirstOrDefault(x => x.CurrencyCode == key.CurrencyCode && x.Category == key.Category)?.PlannedAmount ?? 0,
                expenses.Where(x => x.CurrencyCode == key.CurrencyCode &&
                    x.Category.Trim().ToLowerInvariant() == key.Category).Sum(x => x.Amount)))
            .OrderBy(x => x.CurrencyCode).ThenByDescending(x => x.ActualAmount)
            .ToArray();

        return Results.Ok(new ExpenseIntelligenceResponse(
            tripId, DateTimeOffset.UtcNow, currencies, forecast,
            plannedCategories, categoryComparisons, receiptMismatches, duplicateCandidates,
            expenses.Count, receipts.Count));
    }

    private static string NormalizeTitle(string title) =>
        string.Join(' ', title.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record ExpenseRow(Guid Id, string Title, string Category,
        decimal Amount, string CurrencyCode, DateOnly ExpenseDate, Guid PaidByUserId);
    private sealed record ReceiptRow(Guid Id, Guid? ExpenseId, string? MerchantName,
        DateOnly? ReceiptDate, decimal? TotalAmount, string? CurrencyCode,
        string Status, string FileName);
}

public sealed record ExpenseIntelligenceResponse(
    Guid TripId, DateTimeOffset GeneratedAt, CurrencyActuals[] ActualsByCurrency,
    BudgetForecast Forecast, PlannedCategoryTotal[] PlannedCategoryTotals,
    CategoryComparison[] CategoryComparisons, ReceiptMismatch[] ReceiptMismatches,
    DuplicateExpenseCandidate[] DuplicateExpenseCandidates,
    int ExpenseCount, int ReceiptCount);
public sealed record CurrencyActuals(string CurrencyCode, decimal TotalAmount,
    int ExpenseCount, CategoryActual[] Categories);
public sealed record CategoryActual(string Category, decimal TotalAmount, int ExpenseCount);
public sealed record PlannedCategoryTotal(string CurrencyCode, string Category, decimal PlannedAmount);
public sealed record CategoryComparison(string CurrencyCode, string Category, decimal PlannedAmount, decimal ActualAmount);
public sealed record BudgetForecast(decimal? BudgetAmount, string? CurrencyCode,
    decimal? ActualAmount, decimal? RemainingAmount, decimal? AverageDailySpend,
    decimal? ProjectedTotal, string Status, string Explanation,
    decimal? ProjectedVariance = null, int ElapsedDays = 0, int RemainingDays = 0);
public sealed record ReceiptMismatch(Guid ReceiptId, Guid ExpenseId, string FileName,
    decimal ReceiptAmount, string? ReceiptCurrency, decimal ExpenseAmount,
    string ExpenseCurrency, string Status, string Explanation);
public sealed record DuplicateExpenseCandidate(DateOnly ExpenseDate, decimal Amount,
    string CurrencyCode, DuplicateExpenseItem[] Expenses, string Explanation);
public sealed record DuplicateExpenseItem(Guid ExpenseId, string Title, string Category);
