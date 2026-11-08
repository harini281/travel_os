namespace TravelOS.PlatformApi.Itineraries;

public sealed record GenerateAiDraftRequest(
    int TravellerCount = 1,
    decimal? BudgetAmount = null,
    IReadOnlyList<string>? Interests = null,
    string TravelPace = "balanced");

public sealed record AiPlannerResponse(
    string Status,
    bool Approved,
    string? Message,
    string Provider,
    string? Model,
    AiDraftItinerary? Itinerary,
    IReadOnlyList<string>? Warnings);

public sealed record AiDraftItinerary(
    string Title,
    string Summary,
    IReadOnlyList<AiDraftDay> Days);

public sealed record AiDraftDay(
    int DayNumber,
    DateOnly Date,
    string Theme,
    IReadOnlyList<AiDraftActivity> Activities);

public sealed record AiDraftActivity(
    string Title,
    string? Description,
    string? PlaceName,
    decimal? Latitude,
    decimal? Longitude,
    int? DurationMinutes,
    decimal? EstimatedCost,
    string? CurrencyCode,
    int SortOrder);

public sealed record GeneratedDraftResponse(
    Guid Id,
    Guid TripId,
    string Status,
    DateTimeOffset CreatedAt,
    string Provider,
    string? Model,
    IReadOnlyList<string> Warnings,
    AiDraftItinerary Itinerary);
