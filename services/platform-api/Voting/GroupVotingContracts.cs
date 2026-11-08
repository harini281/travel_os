namespace TravelOS.PlatformApi.Voting;

public sealed record CreateGroupPollRequest(string Title, string? Description, string[] Options, DateTimeOffset? ClosesAt);
public sealed record CastGroupVoteRequest(Guid OptionId);
public sealed record GroupPollOptionResponse(Guid Id, string Label, int VoteCount, bool IsSelected);
public sealed record GroupPollResponse(
    Guid Id, Guid TripId, string Title, string? Description, string Status,
    Guid CreatedByUserId, string CreatedByName, DateTimeOffset CreatedAt,
    DateTimeOffset? ClosesAt, int TotalVotes, Guid? MyVoteOptionId,
    IReadOnlyList<GroupPollOptionResponse> Options);
