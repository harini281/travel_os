namespace TravelOS.PlatformApi.Itineraries;

public sealed record CreateItineraryRequest();

public sealed record AddItineraryDayRequest(
    int DayNumber,
    DateOnly Date);

public sealed record ActivityRequest(
    string Title,
    string? Description,
    string? PlaceName,
    decimal? Latitude,
    decimal? Longitude,
    int? DurationMinutes,
    decimal? EstimatedCost,
    string? CurrencyCode,
    int SortOrder = 0);

public sealed record ActivityResponse(
    Guid Id,
    string Title,
    string? Description,
    string? PlaceName,
    decimal? Latitude,
    decimal? Longitude,
    int? DurationMinutes,
    decimal? EstimatedCost,
    string? CurrencyCode,
    int SortOrder);

public sealed record ItineraryDayResponse(
    Guid Id,
    int DayNumber,
    DateOnly Date,
    IReadOnlyList<ActivityResponse> Activities);

public sealed record ItineraryResponse(
    Guid Id,
    Guid TripId,
    int Version,
    bool IsApproved,
    DateTimeOffset? ApprovedAt,
    IReadOnlyList<ItineraryDayResponse> Days);
