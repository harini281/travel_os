using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Expenses;

public static class ExpenseEndpoints
{
    private static readonly HashSet<string> Categories =
        new(StringComparer.OrdinalIgnoreCase)
        { "accommodation", "transport", "food", "activities", "shopping", "other" };

    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/expenses")
            .WithTags("Shared expenses")
            .RequireAuthorization();
        group.MapGet("/", ListAsync).WithName("ListTripExpenses");
        group.MapPost("/", CreateAsync).WithName("CreateTripExpense");
        group.MapPut("/{expenseId:guid}", UpdateAsync).WithName("UpdateTripExpense");
        group.MapGet("/{expenseId:guid}/history", HistoryAsync).WithName("GetTripExpenseHistory");
        group.MapDelete("/{expenseId:guid}", DeleteAsync).WithName("DeleteTripExpense");
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


    private static string Snapshot(TripExpense expense) => JsonSerializer.Serialize(new
    {
        expense.Title, expense.Category, expense.Notes, expense.Amount,
        expense.CurrencyCode, expense.ExpenseDate, expense.PaidByUserId,
        Shares = expense.Shares.OrderBy(x => x.UserId)
            .Select(x => new { x.UserId, x.ShareAmount }).ToArray()
    });

    private static bool TryAllocate(
        decimal amount, Guid[] participants, string splitType,
        ExpenseCustomShareRequest[] customShares,
        out Dictionary<Guid, decimal> allocations,
        out Dictionary<string, string[]> errors)
    {
        allocations = [];
        errors = [];
        if (splitType is not ("equal" or "percentage" or "exact"))
            errors["splitType"] = ["Split type must be equal, percentage or exact."];
        if (participants.Length is < 1 or > 30)
            errors["participantUserIds"] = ["Choose between 1 and 30 trip members."];
        if (participants.Distinct().Count() != participants.Length)
            errors["participantUserIds"] = ["Participants must not contain duplicates."];
        if (splitType == "equal" && customShares.Length > 0)
            errors["customShares"] = ["Equal splits do not accept custom shares."];
        if (splitType != "equal" && (customShares.Length != participants.Length
            || customShares.Select(x => x.UserId).Distinct().Count() != customShares.Length
            || !customShares.Select(x => x.UserId).OrderBy(x => x)
                .SequenceEqual(participants.OrderBy(x => x))))
            errors["customShares"] = ["Provide exactly one custom share for each selected participant."];
        if (splitType != "equal" && customShares.Any(x => x.Value <= 0m))
            errors["customShares"] = ["Every custom share must be positive."];
        if (splitType == "percentage" && customShares.All(x => x.Value > 0m && x.Value <= 100m)
            && customShares.Sum(x => x.Value) != 100m)
            errors["customShares"] = ["Percentage shares must add up to exactly 100%."];
        if (splitType == "exact" && customShares.All(x => x.Value > 0m && x.Value <= amount)
            && decimal.Round(customShares.Sum(x => x.Value), 4, MidpointRounding.AwayFromZero)
                != amount)
            errors["customShares"] = ["Exact shares must add up to the expense total."];
        if (errors.Count > 0) return false;

        var ordered = participants.OrderBy(x => x).ToArray();
        if (splitType == "equal")
        {
            var share = decimal.Truncate((amount / ordered.Length) * 10000m) / 10000m;
            var remainder = amount - share * ordered.Length;
            for (var i = 0; i < ordered.Length; i++)
                allocations[ordered[i]] = share + (i == 0 ? remainder : 0m);
        }
        else if (splitType == "exact")
        {
            foreach (var item in customShares)
                allocations[item.UserId] = decimal.Round(item.Value, 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            foreach (var item in customShares)
                allocations[item.UserId] = decimal.Round(amount * item.Value / 100m, 4, MidpointRounding.AwayFromZero);
            allocations[ordered[0]] += amount - allocations.Values.Sum();
        }

        if (allocations.Values.Any(x => x <= 0m) || allocations.Values.Sum() != amount)
        {
            errors["customShares"] = ["Every share must be positive and all shares must equal the expense total after rounding."];
            return false;
        }
        return true;
    }

    private static async Task<bool> HasConfirmedRepaymentsAsync(
        Guid tripId, string currencyCode, TravelOsDbContext db, CancellationToken ct) =>
        await db.TripRepayments.AnyAsync(
            x => x.TripId == tripId && x.CurrencyCode == currencyCode && x.Status == "confirmed", ct);

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var expenses = await db.TripExpenses.AsNoTracking()
            .Include(x => x.Shares)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        var userIds = expenses.SelectMany(x => x.Shares.Select(s => s.UserId))
            .Concat(expenses.Select(x => x.PaidByUserId))
            .Where(x => x != Guid.Empty).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var response = expenses.Select(expense => new TripExpenseResponse(
            expense.Id, expense.TripId, expense.Title, expense.Category, expense.Notes,
            expense.Amount, expense.CurrencyCode, expense.ExpenseDate,
            expense.PaidByUserId, names.GetValueOrDefault(expense.PaidByUserId, "Traveller"),
            expense.CreatedByUserId, expense.CreatedAt,
            expense.Shares.OrderBy(x => names.GetValueOrDefault(x.UserId, "Traveller"))
                .Select(share => new TripExpenseShareResponse(
                    share.UserId, names.GetValueOrDefault(share.UserId, "Traveller"), share.ShareAmount))
                .ToArray()
        )).ToArray();

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateAsync(
        Guid tripId, CreateTripExpenseRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var title = request.Title?.Trim() ?? "";
        var category = request.Category?.Trim().ToLowerInvariant() ?? "";
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
        var participants = (request.ParticipantUserIds ?? []).Distinct().ToArray();
        var splitType = request.SplitType?.Trim().ToLowerInvariant() ?? "equal";
        var customShares = request.CustomShares ?? [];
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 2 or > 160) errors["title"] = ["Expense title must be between 2 and 160 characters."];
        if (!Categories.Contains(category)) errors["category"] = ["Choose a supported expense category."];
        if (request.Notes?.Length > 500) errors["notes"] = ["Notes must be 500 characters or fewer."];
        if (request.Amount <= 0 || request.Amount > 999999999999999.9999m)
            errors["amount"] = ["Amount must be greater than zero and within the supported limit."];
        if (!System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];
        if (participants.Length is < 1 or > 30)
            errors["participantUserIds"] = ["Choose between 1 and 30 trip members to split this expense."];
        if (splitType is not ("equal" or "percentage" or "exact"))
            errors["splitType"] = ["Split type must be equal, percentage or exact."];
        if (splitType == "equal" && customShares.Length > 0)
            errors["customShares"] = ["Custom shares are only accepted for percentage or exact splits."];
        if (splitType != "equal" && customShares.Length != participants.Length)
            errors["customShares"] = ["Provide exactly one custom share for every selected participant."];
        if (splitType != "equal" && (customShares.Select(x => x.UserId).Distinct().Count() != customShares.Length
            || !customShares.Select(x => x.UserId).OrderBy(x => x).SequenceEqual(participants.OrderBy(x => x))))
            errors["customShares"] = ["Custom share user IDs must match the selected participants exactly, without duplicates."];
        if (splitType != "equal" && customShares.Any(x => x.Value <= 0))
            errors["customShares"] = ["Every custom share value must be greater than zero."];
        if (splitType == "percentage" && customShares.Any(x => x.Value > 100m))
            errors["customShares"] = ["A participant's percentage cannot exceed 100%."];
        if (splitType == "exact" && customShares.Any(x => x.Value > request.Amount))
            errors["customShares"] = ["An individual share cannot exceed the expense total."];
        if (splitType == "percentage" && customShares.All(x => x.Value > 0m && x.Value <= 100m)
            && customShares.Sum(x => x.Value) != 100m)
            errors["customShares"] = ["Percentage shares must add up to exactly 100%."];
        if (splitType == "exact" && request.Amount > 0m && request.Amount <= 999999999999999.9999m
            && customShares.All(x => x.Value > 0m && x.Value <= request.Amount)
            && decimal.Round(customShares.Sum(x => x.Value), 4, MidpointRounding.AwayFromZero)
                != decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero))
            errors["customShares"] = ["Exact shares must add up to the expense total."];
        if (!participants.Contains(request.PaidByUserId))
            errors["paidByUserId"] = ["The person who paid must be included in the split."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var validMembers = await db.TripMembers.AsNoTracking()
            .Where(x => x.TripId == tripId && participants.Contains(x.UserId))
            .Select(x => x.UserId).ToListAsync(ct);
        if (validMembers.Count != participants.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["participantUserIds"] = ["Every participant must be a current member of this trip."]
            });

        var roundedAmount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);
        if (roundedAmount <= 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["amount"] = ["Amount is too small for the supported precision."]
            });

        var expense = new TripExpense
        {
            TripId = tripId, PaidByUserId = request.PaidByUserId,
            CreatedByUserId = userId, Title = title, Category = category,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Amount = roundedAmount,
            CurrencyCode = currency, ExpenseDate = request.ExpenseDate
        };

        var orderedParticipants = participants.OrderBy(x => x).ToArray();
        var allocations = new Dictionary<Guid, decimal>();
        if (splitType == "equal")
        {
            var baseShare = decimal.Truncate((expense.Amount / orderedParticipants.Length) * 10000m) / 10000m;
            var remainder = expense.Amount - (baseShare * orderedParticipants.Length);
            for (var i = 0; i < orderedParticipants.Length; i++)
                allocations[orderedParticipants[i]] = baseShare + (i == 0 ? remainder : 0m);
        }
        else if (splitType == "exact")
        {
            foreach (var share in customShares)
                allocations[share.UserId] = decimal.Round(share.Value, 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            foreach (var share in customShares)
                allocations[share.UserId] = decimal.Round(
                    expense.Amount * share.Value / 100m, 4, MidpointRounding.AwayFromZero);
            var percentageRemainder = expense.Amount - allocations.Values.Sum();
            allocations[orderedParticipants[0]] += percentageRemainder;
        }

        if (allocations.Values.Any(x => x <= 0m) || allocations.Values.Sum() != expense.Amount)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["customShares"] = ["Each participant must receive a positive share and all rounded shares must add up exactly to the expense total."]
            });

        foreach (var participantId in orderedParticipants)
        {
            expense.Shares.Add(new TripExpenseShare
            {
                ExpenseId = expense.Id,
                UserId = participantId,
                ShareAmount = allocations[participantId]
            });
        }

        db.TripExpenses.Add(expense);
        db.TripExpenseAudits.Add(new TripExpenseAudit
        {
            TripId = tripId, ExpenseId = expense.Id, ChangedByUserId = userId,
            Action = "created", BeforeJson = "{}", AfterJson = Snapshot(expense)
        });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/trips/{tripId}/expenses/{expense.Id}",
            new { expense.Id, expense.Amount, expense.CurrencyCode, participantCount = expense.Shares.Count });
    }


    private static async Task<IResult> UpdateAsync(
        Guid tripId, Guid expenseId, UpdateTripExpenseRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var expense = await db.TripExpenses.Include(x => x.Shares)
            .FirstOrDefaultAsync(x => x.Id == expenseId && x.TripId == tripId, ct);
        if (expense is null) return Results.NotFound();

        var isOwner = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
        if (expense.CreatedByUserId != userId && !isOwner) return Results.Forbid();

        var title = request.Title?.Trim() ?? "";
        var category = request.Category?.Trim().ToLowerInvariant() ?? "";
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
        var amount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);
        var participants = request.ParticipantUserIds ?? [];
        var splitType = request.SplitType?.Trim().ToLowerInvariant() ?? "exact";
        var customShares = request.CustomShares ?? [];
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 2 or > 160) errors["title"] = ["Expense title must be between 2 and 160 characters."];
        if (!Categories.Contains(category)) errors["category"] = ["Choose a supported expense category."];
        if (request.Notes?.Length > 500) errors["notes"] = ["Notes must be 500 characters or fewer."];
        if (request.Amount <= 0m || request.Amount > 999999999999999.9999m || amount <= 0m)
            errors["amount"] = ["Amount must be greater than zero and within the supported limit."];
        if (!System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];
        if (request.PaidByUserId == Guid.Empty || !participants.Contains(request.PaidByUserId))
            errors["paidByUserId"] = ["The person who paid must be included in the split."];
        if (request.ExpenseDate == default)
            errors["expenseDate"] = ["A valid expense date is required."];
        if (errors.Count == 0 && !TryAllocate(amount, participants, splitType, customShares, out var allocations, out var splitErrors))
            foreach (var pair in splitErrors) errors[pair.Key] = pair.Value;
        else if (errors.Count == 0)
            await Task.CompletedTask;

        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await HasConfirmedRepaymentsAsync(tripId, expense.CurrencyCode, db, ct)
            || await HasConfirmedRepaymentsAsync(tripId, currency, db, ct))
            return Results.Conflict(new { message = "This expense cannot be edited because a repayment has been confirmed in its currency. Add an adjustment expense instead." });

        var validMembers = await db.TripMembers.AsNoTracking()
            .Where(x => x.TripId == tripId && participants.Contains(x.UserId))
            .Select(x => x.UserId).ToListAsync(ct);
        if (validMembers.Count != participants.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["participantUserIds"] = ["Every participant must be a current trip member."]
            });

        // Recalculate after the member check using the validated request.
        TryAllocate(amount, participants, splitType, customShares, out var shares, out _);
        var before = Snapshot(expense);
        expense.Title = title;
        expense.Category = category;
        expense.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        expense.Amount = amount;
        expense.CurrencyCode = currency;
        expense.ExpenseDate = request.ExpenseDate;
        expense.PaidByUserId = request.PaidByUserId;
        db.TripExpenseShares.RemoveRange(expense.Shares);
        expense.Shares.Clear();
        foreach (var item in shares)
            expense.Shares.Add(new TripExpenseShare
            {
                ExpenseId = expense.Id, UserId = item.Key, ShareAmount = item.Value
            });

        db.TripExpenseAudits.Add(new TripExpenseAudit
        {
            TripId = tripId, ExpenseId = expense.Id, ChangedByUserId = userId,
            Action = "updated", BeforeJson = before, AfterJson = Snapshot(expense)
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { expense.Id, expense.Amount, expense.CurrencyCode, updated = true });
    }

    private static async Task<IResult> HistoryAsync(
        Guid tripId, Guid expenseId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var exists = await db.TripExpenses.AsNoTracking()
            .AnyAsync(x => x.Id == expenseId && x.TripId == tripId, ct);
        var audits = await db.TripExpenseAudits.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ExpenseId == expenseId)
            .OrderByDescending(x => x.ChangedAt).ToListAsync(ct);
        if (!exists && audits.Count == 0) return Results.NotFound();

        var actorIds = audits.Select(x => x.ChangedByUserId).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => actorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return Results.Ok(audits.Select(x => new TripExpenseAuditResponse(
            x.Id, x.ExpenseId, x.Action, x.ChangedByUserId,
            names.GetValueOrDefault(x.ChangedByUserId, "Traveller"),
            x.ChangedAt, x.BeforeJson, x.AfterJson)).ToArray());
    }

    private static async Task<IResult> DeleteAsync(
        Guid tripId, Guid expenseId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var expense = await db.TripExpenses.FirstOrDefaultAsync(
            x => x.Id == expenseId && x.TripId == tripId, ct);
        if (expense is null) return Results.NotFound();
        var hasConfirmedRepayments = await db.TripRepayments.AnyAsync(
            x => x.TripId == tripId && x.CurrencyCode == expense.CurrencyCode && x.Status == "confirmed", ct);
        if (hasConfirmedRepayments)
            return Results.Conflict(new { message = "Expenses in this currency cannot be removed after a repayment has been confirmed. Add an adjustment expense instead." });
        var isOwner = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
        if (expense.CreatedByUserId != userId && !isOwner) return Results.Forbid();

        db.TripExpenseAudits.Add(new TripExpenseAudit
        {
            TripId = tripId, ExpenseId = expense.Id, ChangedByUserId = userId,
            Action = "deleted", BeforeJson = Snapshot(expense), AfterJson = "{}"
        });
        db.TripExpenses.Remove(expense);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
