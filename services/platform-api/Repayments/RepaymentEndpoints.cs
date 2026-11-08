using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Repayments;

public static class RepaymentEndpoints
{
    public static IEndpointRouteBuilder MapRepaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/repayments")
            .WithTags("Repayment tracking")
            .RequireAuthorization();
        group.MapGet("/", ListAsync).WithName("ListTripRepayments");
        group.MapPost("/", CreateAsync).WithName("CreateTripRepayment");
        group.MapPost("/{repaymentId:guid}/respond", RespondAsync).WithName("RespondToTripRepayment");
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

    private static async Task<decimal> GetNetBalanceAsync(
        Guid tripId, Guid userId, string currency, TravelOsDbContext db, CancellationToken ct)
    {
        var expenses = await db.TripExpenses.AsNoTracking()
            .Include(x => x.Shares)
            .Where(x => x.TripId == tripId && x.CurrencyCode == currency)
            .ToListAsync(ct);
        var net = expenses.Where(x => x.PaidByUserId == userId).Sum(x => x.Amount)
            - expenses.SelectMany(x => x.Shares).Where(x => x.UserId == userId).Sum(x => x.ShareAmount);

        var confirmed = await db.TripRepayments.AsNoTracking()
            .Where(x => x.TripId == tripId && x.CurrencyCode == currency && x.Status == "confirmed"
                && (x.FromUserId == userId || x.ToUserId == userId))
            .ToListAsync(ct);
        net += confirmed.Where(x => x.FromUserId == userId).Sum(x => x.Amount);
        net -= confirmed.Where(x => x.ToUserId == userId).Sum(x => x.Amount);
        return decimal.Round(net, 4, MidpointRounding.AwayFromZero);
    }

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var records = await db.TripRepayments.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var ids = records.SelectMany(x => new[] { x.FromUserId, x.ToUserId }).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return Results.Ok(records.Select(x => new RepaymentResponse(
            x.Id, x.TripId, x.FromUserId, names.GetValueOrDefault(x.FromUserId, "Traveller"),
            x.ToUserId, names.GetValueOrDefault(x.ToUserId, "Traveller"),
            x.Amount, x.CurrencyCode, x.Status, x.Note, x.CreatedAt, x.RespondedAt)).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        Guid tripId, CreateRepaymentRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
        var note = request.Note?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (request.ToUserId == Guid.Empty || request.ToUserId == userId)
            errors["toUserId"] = ["Choose another trip member as the repayment recipient."];
        if (request.Amount <= 0 || request.Amount > 999999999999999.9999m)
            errors["amount"] = ["Repayment amount must be greater than zero and within the supported limit."];
        if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];
        if (note?.Length > 500) errors["note"] = ["Note must be 500 characters or fewer."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!await IsMemberAsync(tripId, request.ToUserId, db, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["toUserId"] = ["Recipient must be a current member of this trip."]
            });

        var amount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["amount"] = ["Amount is too small for the supported precision."]
            });

        var debtorBalance = await GetNetBalanceAsync(tripId, userId, currency, db, ct);
        var creditorBalance = await GetNetBalanceAsync(tripId, request.ToUserId, currency, db, ct);
        var reservedOutgoing = await db.TripRepayments.Where(x =>
            x.TripId == tripId && x.CurrencyCode == currency && x.Status == "pending" && x.FromUserId == userId)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var reservedIncoming = await db.TripRepayments.Where(x =>
            x.TripId == tripId && x.CurrencyCode == currency && x.Status == "pending" && x.ToUserId == request.ToUserId)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var debtAvailable = -debtorBalance - reservedOutgoing;
        var creditAvailable = creditorBalance - reservedIncoming;

        if (debtAvailable <= 0 || creditAvailable <= 0 || amount > debtAvailable || amount > creditAvailable)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["amount"] = ["This repayment exceeds the currently outstanding debt or recipient's credit in this currency. Refresh the settlement summary and try again."]
            });

        var repayment = new TripRepayment
        {
            TripId = tripId, FromUserId = userId, ToUserId = request.ToUserId,
            CreatedByUserId = userId, Amount = amount, CurrencyCode = currency,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        };
        db.TripRepayments.Add(repayment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/trips/{tripId}/repayments/{repayment.Id}",
            new { repayment.Id, repayment.Amount, repayment.CurrencyCode, repayment.Status });
    }

    private static async Task<IResult> RespondAsync(
        Guid tripId, Guid repaymentId, RespondToRepaymentRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var decision = request.Decision?.Trim().ToLowerInvariant();
        if (decision is not ("confirm" or "reject"))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["decision"] = ["Decision must be 'confirm' or 'reject'."]
            });

        var repayment = await db.TripRepayments.FirstOrDefaultAsync(
            x => x.Id == repaymentId && x.TripId == tripId, ct);
        if (repayment is null) return Results.NotFound();
        if (repayment.ToUserId != userId) return Results.Forbid();
        if (repayment.Status != "pending")
            return Results.Conflict(new { message = "This repayment has already been responded to." });

        if (decision == "confirm")
        {
            var debtorBalance = await GetNetBalanceAsync(tripId, repayment.FromUserId, repayment.CurrencyCode, db, ct);
            var creditorBalance = await GetNetBalanceAsync(tripId, repayment.ToUserId, repayment.CurrencyCode, db, ct);
            if (debtorBalance >= 0 || creditorBalance <= 0
                || repayment.Amount > -debtorBalance || repayment.Amount > creditorBalance)
                return Results.Conflict(new { message = "The underlying debt has changed. Refresh the settlement summary before confirming." });
            repayment.Status = "confirmed";
        }
        else
        {
            repayment.Status = "rejected";
        }

        repayment.RespondedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { repayment.Id, repayment.Status, repayment.RespondedAt });
    }
}
