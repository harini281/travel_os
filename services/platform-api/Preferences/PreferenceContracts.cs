namespace TravelOS.PlatformApi.Preferences;

public sealed record SaveTripPreferenceRequest(
    string[] Interests,
    string Pace,
    string TravelStyle,
    decimal? DailyBudgetAmount,
    string? DailyBudgetCurrencyCode,
    string? Notes);

public sealed record MyTripPreferenceResponse(
    Guid Id, Guid TripId, Guid UserId, string[] Interests,
    string Pace, string TravelStyle, decimal? DailyBudgetAmount,
    string? DailyBudgetCurrencyCode, string? Notes, DateTimeOffset UpdatedAt);

public sealed record GroupTripPreferenceResponse(
    Guid UserId, string DisplayName, string[] Interests,
    string Pace, string TravelStyle, decimal? DailyBudgetAmount,
    string? DailyBudgetCurrencyCode, DateTimeOffset UpdatedAt);
