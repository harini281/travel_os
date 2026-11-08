using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Groups;

public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/invitations", ListMyInvitationsAsync)
            .WithTags("Group collaboration")
            .RequireAuthorization()
            .WithName("ListMyTripInvitations");

        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}")
            .WithTags("Group collaboration")
            .RequireAuthorization();

        group.MapGet("/members", ListMembersAsync).WithName("ListTripMembers");
        group.MapPost("/invitations", CreateInvitationAsync).WithName("CreateTripInvitation");
        group.MapGet("/invitations", ListInvitationsAsync).WithName("ListTripInvitations");
        group.MapDelete("/invitations/{invitationId:guid}", RevokeInvitationAsync).WithName("RevokeTripInvitation");
        group.MapPost("/invitations/{invitationId:guid}/accept", AcceptInvitationAsync).WithName("AcceptTripInvitation");
        group.MapDelete("/members/{memberUserId:guid}", RemoveMemberAsync).WithName("RemoveTripMember");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<bool> IsTripOwnerAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);

    private static async Task<bool> IsTripMemberAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);


    private static async Task<IResult> ListMyInvitationsAsync(
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var now = DateTimeOffset.UtcNow;
        var expired = await db.TripInvitations
            .Where(x => x.InvitedUserId == userId && x.Status == "pending" && x.ExpiresAt <= now)
            .ToListAsync(ct);
        foreach (var invitation in expired) invitation.Status = "expired";
        if (expired.Count > 0) await db.SaveChangesAsync(ct);

        var invitations = await (
            from invitation in db.TripInvitations.AsNoTracking()
            join trip in db.Trips.AsNoTracking() on invitation.TripId equals trip.Id
            join inviter in db.Users.AsNoTracking() on invitation.InvitedByUserId equals inviter.Id
            where invitation.InvitedUserId == userId && invitation.Status == "pending"
            orderby invitation.CreatedAt descending
            select new MyTripInvitationResponse(
                invitation.Id, trip.Id, trip.Name, trip.Destination,
                inviter.DisplayName, invitation.CreatedAt, invitation.ExpiresAt)
        ).ToListAsync(ct);
        return Results.Ok(invitations);
    }

    private static async Task<IResult> ListMembersAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsTripMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var members = await (
            from member in db.TripMembers.AsNoTracking()
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            where member.TripId == tripId
            orderby member.Role == "owner" descending, member.JoinedAt
            select new TripMemberResponse(
                user.Id, user.DisplayName, user.Email!, member.Role, member.JoinedAt)
        ).ToListAsync(ct);

        return Results.Ok(members);
    }

    private static async Task<IResult> CreateInvitationAsync(
        Guid tripId, CreateTripInvitationRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsTripOwnerAsync(tripId, userId, db, ct)) return Results.NotFound();

        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 ||
            !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Enter a valid email address."]
            });

        var invitedUser = await db.Users.FirstOrDefaultAsync(
            x => x.Email != null && x.Email.ToLower() == email.ToLower(), ct);
        if (invitedUser is null)
            return Results.NotFound(new { message = "This email is not registered with TravelOS yet. Ask the traveller to create an account first." });
        if (invitedUser.Id == userId)
            return Results.Conflict(new { message = "You already own this trip." });
        if (await IsTripMemberAsync(tripId, invitedUser.Id, db, ct))
            return Results.Conflict(new { message = "This traveller is already a trip member." });

        var existing = await db.TripInvitations.FirstOrDefaultAsync(
            x => x.TripId == tripId && x.InvitedUserId == invitedUser.Id && x.Status == "pending", ct);
        if (existing is not null && existing.ExpiresAt > DateTimeOffset.UtcNow)
            return Results.Conflict(new { message = "A pending invitation already exists for this traveller." });
        if (existing is not null) existing.Status = "expired";

        var invitation = new TripInvitation
        {
            TripId = tripId,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = userId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        db.TripInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/trips/{tripId}/invitations/{invitation.Id}",
            new { invitation.Id, invitation.TripId, invitedUser.Email, invitation.Status, invitation.CreatedAt, invitation.ExpiresAt });
    }

    private static async Task<IResult> ListInvitationsAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsTripOwnerAsync(tripId, userId, db, ct)) return Results.NotFound();

        var invitations = await (
            from invitation in db.TripInvitations.AsNoTracking()
            join trip in db.Trips.AsNoTracking() on invitation.TripId equals trip.Id
            join invitedBy in db.Users.AsNoTracking() on invitation.InvitedByUserId equals invitedBy.Id
            where invitation.TripId == tripId
            orderby invitation.CreatedAt descending
            select new TripInvitationResponse(
                invitation.Id, trip.Id, trip.Name, trip.Destination,
                invitedBy.DisplayName, invitation.Status, invitation.CreatedAt, invitation.ExpiresAt)
        ).ToListAsync(ct);

        return Results.Ok(invitations);
    }

    private static async Task<IResult> RevokeInvitationAsync(
        Guid tripId, Guid invitationId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsTripOwnerAsync(tripId, userId, db, ct)) return Results.NotFound();

        var invitation = await db.TripInvitations.FirstOrDefaultAsync(
            x => x.Id == invitationId && x.TripId == tripId, ct);
        if (invitation is null) return Results.NotFound();
        if (invitation.Status != "pending")
            return Results.Conflict(new { message = "Only pending invitations can be revoked." });

        invitation.Status = "revoked";
        invitation.RespondedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AcceptInvitationAsync(
        Guid tripId, Guid invitationId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var invitation = await db.TripInvitations.FirstOrDefaultAsync(
            x => x.Id == invitationId && x.TripId == tripId && x.InvitedUserId == userId, ct);
        if (invitation is null) return Results.NotFound();
        if (invitation.Status != "pending")
            return Results.Conflict(new { message = "This invitation is no longer pending." });
        if (invitation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            invitation.Status = "expired";
            await db.SaveChangesAsync(ct);
            return Results.Conflict(new { message = "This invitation has expired." });
        }
        if (await IsTripMemberAsync(tripId, userId, db, ct))
        {
            invitation.Status = "accepted";
            invitation.RespondedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "You are already a member of this trip." });
        }

        db.TripMembers.Add(new TripMember
        {
            TripId = tripId, UserId = userId, Role = "member"
        });
        invitation.Status = "accepted";
        invitation.RespondedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { message = "You have joined the trip." });
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid tripId, Guid memberUserId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsTripOwnerAsync(tripId, userId, db, ct)) return Results.NotFound();
        if (memberUserId == userId)
            return Results.Conflict(new { message = "The trip owner cannot remove themselves. Transfer ownership or delete the trip instead." });

        var member = await db.TripMembers.FirstOrDefaultAsync(
            x => x.TripId == tripId && x.UserId == memberUserId, ct);
        if (member is null) return Results.NotFound();

        db.TripMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
