namespace TravelOS.PlatformApi.Groups;

public sealed record CreateTripInvitationRequest(string Email);
public sealed record TripInvitationResponse(
    Guid Id, Guid TripId, string TripName, string Destination,
    string InvitedByName, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
public sealed record TripMemberResponse(
    Guid UserId, string DisplayName, string Email, string Role, DateTimeOffset JoinedAt);

public sealed record MyTripInvitationResponse(
    Guid Id, Guid TripId, string TripName, string Destination,
    string InvitedByName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
