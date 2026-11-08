namespace TravelOS.PlatformApi.Domain;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public string Destination { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<TripMember> Members { get; set; } = new List<TripMember>();
}

public sealed class TripMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Itinerary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public int Version { get; set; } = 1;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public ICollection<ItineraryDay> Days { get; set; } = new List<ItineraryDay>();
}

public sealed class ItineraryDay
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItineraryId { get; set; }
    public int DayNumber { get; set; }
    public DateOnly Date { get; set; }
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}

public sealed class Activity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItineraryDayId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? PlaceName { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? CurrencyCode { get; set; }
    public int SortOrder { get; set; }
}

public sealed class GeneratedItineraryDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string Status { get; set; } = "draft";
    public string ContractVersion { get; set; } = "1.0";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
}

public sealed class BudgetItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BudgetId { get; set; }
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
}
