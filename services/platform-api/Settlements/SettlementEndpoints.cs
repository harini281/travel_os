using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Settlements;

public static class SettlementEndpoints
{
    private sealed class MutableBalance(Guid userId, string name, decimal net)
    {
        public Guid UserId { get; } = userId;
        public string Name { get; } = name;
        public decimal Net { get; set; } = net;
    }

    public static IEndpointRouteBuilder MapSettlementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/trips/{tripId:guid}/settlements", GetSettlementAsync)
            .WithTags("Debt settlement")
            .RequireAuthorization()
            .WithName("GetTripSettlement");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<IResult> GetSettlementAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var isMember = await db.TripMembers.AnyAsync(
            x => x.TripId == tripId && x.UserId == userId, ct);
        if (!isMember) return Results.NotFound();

        var expenses = await db.TripExpenses.AsNoTracking()
            .Include(x => x.Shares)
            .Where(x => x.TripId == tripId)
            .ToListAsync(ct);
        var repayments = await db.TripRepayments.AsNoTracking()
            .Where(x => x.TripId == tripId && x.Status == "confirmed").ToListAsync(ct);
        var userIds = expenses.SelectMany(x => x.Shares.Select(s => s.UserId))
            .Concat(expenses.Select(x => x.PaidByUserId))
            .Concat(repayments.SelectMany(x => new[] { x.FromUserId, x.ToUserId }))
            .Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var currencies = expenses.GroupBy(x => x.CurrencyCode)
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var nets = new Dictionary<Guid, decimal>();
                foreach (var expense in group)
                {
                    nets[expense.PaidByUserId] = nets.GetValueOrDefault(expense.PaidByUserId) + expense.Amount;
                    foreach (var share in expense.Shares)
                        nets[share.UserId] = nets.GetValueOrDefault(share.UserId) - share.ShareAmount;
                }

                foreach (var repayment in repayments.Where(x => x.CurrencyCode == group.Key))
                {
                    nets[repayment.FromUserId] = nets.GetValueOrDefault(repayment.FromUserId) + repayment.Amount;
                    nets[repayment.ToUserId] = nets.GetValueOrDefault(repayment.ToUserId) - repayment.Amount;
                }

                var balances = nets
                    .Select(x => new MutableBalance(x.Key, names.GetValueOrDefault(x.Key, "Traveller"), decimal.Round(x.Value, 4)))
                    .Where(x => x.Net != 0m)
                    .OrderBy(x => x.UserId)
                    .ToList();

                var debtors = balances.Where(x => x.Net < 0m)
                    .Select(x => new MutableBalance(x.UserId, x.Name, -x.Net))
                    .OrderByDescending(x => x.Net).ToList();
                var creditors = balances.Where(x => x.Net > 0m)
                    .Select(x => new MutableBalance(x.UserId, x.Name, x.Net))
                    .OrderByDescending(x => x.Net).ToList();

                var transfers = new List<SettlementTransferResponse>();
                var debtorIndex = 0;
                var creditorIndex = 0;
                while (debtorIndex < debtors.Count && creditorIndex < creditors.Count)
                {
                    var debtor = debtors[debtorIndex];
                    var creditor = creditors[creditorIndex];
                    var amount = decimal.Round(Math.Min(debtor.Net, creditor.Net), 4);
                    if (amount > 0m)
                    {
                        transfers.Add(new SettlementTransferResponse(
                            debtor.UserId, debtor.Name, creditor.UserId, creditor.Name, amount));
                        debtor.Net -= amount;
                        creditor.Net -= amount;
                    }
                    if (debtor.Net == 0m) debtorIndex++;
                    if (creditor.Net == 0m) creditorIndex++;
                }

                return new CurrencySettlementResponse(
                    group.Key, group.Sum(x => x.Amount),
                    balances.Select(x => new SettlementBalanceResponse(x.UserId, x.Name, x.Net)).ToArray(),
                    transfers);
            }).ToArray();

        return Results.Ok(new TripSettlementResponse(tripId, expenses.Count, currencies));
    }
}
