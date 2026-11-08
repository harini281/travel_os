namespace TravelOS.PlatformApi.Repayments;

public sealed record CreateRepaymentRequest(
    Guid ToUserId, decimal Amount, string CurrencyCode, string? Note);

public sealed record RespondToRepaymentRequest(string Decision);

public sealed record RepaymentResponse(
    Guid Id, Guid TripId, Guid FromUserId, string FromName,
    Guid ToUserId, string ToName, decimal Amount, string CurrencyCode,
    string Status, string? Note, DateTimeOffset CreatedAt, DateTimeOffset? RespondedAt);
