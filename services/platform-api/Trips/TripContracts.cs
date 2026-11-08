namespace TravelOS.PlatformApi.Trips;

public sealed record CreateTripRequest(
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string CurrencyCode = "USD");

public sealed record UpdateTripRequest(
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string CurrencyCode);

public sealed record TripResponse(
    Guid Id,
    string Name,
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string CurrencyCode,
    Guid OwnerUserId,
    string CurrentUserRole,
    int MemberCount,
    DateTimeOffset CreatedAt);
