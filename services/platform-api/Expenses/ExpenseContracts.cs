namespace TravelOS.PlatformApi.Expenses;

public sealed record ExpenseCustomShareRequest(Guid UserId, decimal Value);

public sealed record CreateTripExpenseRequest(
    string Title, string Category, string? Notes, decimal Amount,
    string CurrencyCode, DateOnly ExpenseDate, Guid PaidByUserId,
    Guid[] ParticipantUserIds, string SplitType = "equal",
    ExpenseCustomShareRequest[]? CustomShares = null);

public sealed record TripExpenseShareResponse(Guid UserId, string DisplayName, decimal ShareAmount);
public sealed record TripExpenseResponse(
    Guid Id, Guid TripId, string Title, string Category, string? Notes,
    decimal Amount, string CurrencyCode, DateOnly ExpenseDate,
    Guid PaidByUserId, string PaidByName, Guid CreatedByUserId,
    DateTimeOffset CreatedAt, IReadOnlyList<TripExpenseShareResponse> Shares);

public sealed record UpdateTripExpenseRequest(
    string Title, string Category, string? Notes, decimal Amount,
    string CurrencyCode, DateOnly ExpenseDate, Guid PaidByUserId,
    Guid[] ParticipantUserIds, string SplitType = "exact",
    ExpenseCustomShareRequest[]? CustomShares = null);

public sealed record TripExpenseAuditResponse(
    Guid Id, Guid ExpenseId, string Action, Guid ChangedByUserId,
    string ChangedByName, DateTimeOffset ChangedAt, string BeforeJson, string AfterJson);
