namespace TravelOS.PlatformApi.Settlements;

public sealed record SettlementBalanceResponse(Guid UserId, string DisplayName, decimal NetAmount);
public sealed record SettlementTransferResponse(Guid FromUserId, string FromName, Guid ToUserId, string ToName, decimal Amount);
public sealed record CurrencySettlementResponse(
    string CurrencyCode, decimal TotalExpenses,
    IReadOnlyList<SettlementBalanceResponse> Balances,
    IReadOnlyList<SettlementTransferResponse> SuggestedTransfers);
public sealed record TripSettlementResponse(
    Guid TripId, int ExpenseCount, IReadOnlyList<CurrencySettlementResponse> Currencies);
