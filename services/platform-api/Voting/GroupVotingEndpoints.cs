using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Voting;

public static class GroupVotingEndpoints
{
    public static IEndpointRouteBuilder MapGroupVotingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/polls")
            .WithTags("Group voting")
            .RequireAuthorization();

        group.MapGet("/", ListPollsAsync).WithName("ListTripPolls");
        group.MapPost("/", CreatePollAsync).WithName("CreateTripPoll");
        group.MapPost("/{pollId:guid}/vote", CastVoteAsync).WithName("CastTripPollVote");
        group.MapPost("/{pollId:guid}/close", ClosePollAsync).WithName("CloseTripPoll");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static Task<bool> IsMemberAsync(Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static async Task<IResult> ListPollsAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var expired = await db.GroupPolls
            .Where(x => x.TripId == tripId && x.Status == "open" && x.ClosesAt != null && x.ClosesAt <= now)
            .ToListAsync(ct);
        foreach (var poll in expired) poll.Status = "closed";
        if (expired.Count > 0) await db.SaveChangesAsync(ct);

        var polls = await db.GroupPolls.AsNoTracking()
            .Include(x => x.Options)
            .Include(x => x.Votes)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        var creators = await db.Users.AsNoTracking()
            .Where(x => polls.Select(p => p.CreatedByUserId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var response = polls.Select(poll => new GroupPollResponse(
            poll.Id, poll.TripId, poll.Title, poll.Description, poll.Status,
            poll.CreatedByUserId, creators.GetValueOrDefault(poll.CreatedByUserId, "Traveller"),
            poll.CreatedAt, poll.ClosesAt, poll.Votes.Count,
            poll.Votes.FirstOrDefault(vote => vote.UserId == userId)?.OptionId,
            poll.Options.OrderBy(option => option.SortOrder).Select(option =>
                new GroupPollOptionResponse(
                    option.Id, option.Label,
                    poll.Votes.Count(vote => vote.OptionId == option.Id),
                    poll.Votes.Any(vote => vote.UserId == userId && vote.OptionId == option.Id)
                )).ToArray()
        )).ToArray();

        return Results.Ok(response);
    }

    private static async Task<IResult> CreatePollAsync(
        Guid tripId, CreateGroupPollRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim();
        var options = (request.Options ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 3 or > 160) errors["title"] = ["Poll title must be between 3 and 160 characters."];
        if (description?.Length > 1000) errors["description"] = ["Description must be 1,000 characters or fewer."];
        if (options.Length is < 2 or > 8 || options.Any(x => x.Length > 160))
            errors["options"] = ["Provide 2 to 8 distinct options, each no longer than 160 characters."];
        if (request.ClosesAt.HasValue && request.ClosesAt.Value <= DateTimeOffset.UtcNow)
            errors["closesAt"] = ["Poll closing time must be in the future."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var poll = new GroupPoll
        {
            TripId = tripId, CreatedByUserId = userId, Title = title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            ClosesAt = request.ClosesAt?.ToUniversalTime()
        };
        for (var i = 0; i < options.Length; i++)
            poll.Options.Add(new GroupPollOption { PollId = poll.Id, Label = options[i], SortOrder = i });

        db.GroupPolls.Add(poll);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/trips/{tripId}/polls/{poll.Id}", new { poll.Id, poll.Status });
    }

    private static async Task<IResult> CastVoteAsync(
        Guid tripId, Guid pollId, CastGroupVoteRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var poll = await db.GroupPolls.FirstOrDefaultAsync(x => x.Id == pollId && x.TripId == tripId, ct);
        if (poll is null) return Results.NotFound();
        if (poll.Status != "open") return Results.Conflict(new { message = "This poll is closed." });
        if (poll.ClosesAt.HasValue && poll.ClosesAt <= DateTimeOffset.UtcNow)
        {
            poll.Status = "closed";
            await db.SaveChangesAsync(ct);
            return Results.Conflict(new { message = "Voting has ended for this poll." });
        }
        var optionExists = await db.GroupPollOptions.AnyAsync(
            x => x.Id == request.OptionId && x.PollId == pollId, ct);
        if (!optionExists) return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["optionId"] = ["Choose an option belonging to this poll."]
        });

        var vote = await db.GroupPollVotes.FirstOrDefaultAsync(
            x => x.PollId == pollId && x.UserId == userId, ct);
        if (vote is null)
            db.GroupPollVotes.Add(new GroupPollVote { PollId = pollId, UserId = userId, OptionId = request.OptionId });
        else
        {
            vote.OptionId = request.OptionId;
            vote.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { message = "Your vote has been saved. You can change it while the poll is open." });
    }

    private static async Task<IResult> ClosePollAsync(
        Guid tripId, Guid pollId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var poll = await db.GroupPolls.FirstOrDefaultAsync(x => x.Id == pollId && x.TripId == tripId, ct);
        if (poll is null) return Results.NotFound();
        var isOwner = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
        if (poll.CreatedByUserId != userId && !isOwner) return Results.Forbid();
        if (poll.Status == "closed") return Results.Ok(new { message = "Poll is already closed." });

        poll.Status = "closed";
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { message = "Poll closed. Results are now final." });
    }
}
