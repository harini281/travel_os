namespace TravelOS.PlatformApi.Expenses;

public sealed record CreateTripExpenseRequest(
    string Title, string Category, string? Notes, decimal Amount,
    string CurrencyCode, DateOnly ExpenseDate, Guid PaidByUserId,
    Guid[] ParticipantUserIds);

public sealed record TripExpenseShareResponse(Guid UserId, string DisplayName, decimal ShareAmount);
public sealed record TripExpenseResponse(
    Guid Id, Guid TripId, string Title, string Category, string? Notes,
    decimal Amount, string CurrencyCode, DateOnly ExpenseDate,
    Guid PaidByUserId, string PaidByName, Guid CreatedByUserId,
    DateTimeOffset CreatedAt, IReadOnlyList<TripExpenseShareResponse> Shares);
