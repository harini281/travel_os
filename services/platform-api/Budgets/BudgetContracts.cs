namespace TravelOS.PlatformApi.Budgets;

public sealed record UpsertBudgetRequest(decimal Amount, string CurrencyCode);

public sealed record BudgetItemRequest(
    string Category,
    decimal Amount,
    string? CurrencyCode = null);

public sealed record BudgetItemResponse(
    Guid Id,
    string Category,
    decimal Amount,
    string CurrencyCode);

public sealed record BudgetCurrencyTotal(
    string CurrencyCode,
    decimal PlannedAmount);

public sealed record BudgetResponse(
    Guid Id,
    Guid TripId,
    decimal Amount,
    string CurrencyCode,
    IReadOnlyList<BudgetItemResponse> Items,
    IReadOnlyList<BudgetCurrencyTotal> TotalsByCurrency);
