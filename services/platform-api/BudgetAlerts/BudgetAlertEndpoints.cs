using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.BudgetAlerts;

public static class BudgetAlertEndpoints
{
    private static readonly int[] Thresholds = [50, 75, 90, 100];

    public static IEndpointRouteBuilder MapBudgetAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/budget-alerts")
            .WithTags("Budget alerts").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapPost("/evaluate", EvaluateAsync);
        group.MapPost("/{alertId:guid}/read", MarkReadAsync);
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<bool> IsMemberAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var alerts = await db.TripBudgetAlerts.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.TriggeredAt)
            .Select(x => new
            {
                x.Id, x.AlertType, x.ThresholdPercent, x.BudgetAmountSnapshot,
                x.CurrencyCode, x.ActualAmountSnapshot, x.ProjectedAmountSnapshot,
                x.Message, x.TriggeredAt,
                IsRead = db.TripBudgetAlertReads.Any(read =>
                    read.AlertId == x.Id && read.UserId == userId)
            }).ToListAsync(ct);
        return Results.Ok(alerts);
    }

    private static async Task<IResult> MarkReadAsync(
        Guid tripId, Guid alertId, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var exists = await db.TripBudgetAlerts.AnyAsync(
            x => x.Id == alertId && x.TripId == tripId, ct);
        if (!exists) return Results.NotFound();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""TripBudgetAlertReads"" (""AlertId"", ""UserId"", ""ReadAt"")
            VALUES ({alertId}, {userId}, now())
            ON CONFLICT (""AlertId"", ""UserId"") DO NOTHING", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> EvaluateAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();

        var budget = await db.Budgets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        if (budget is null) return Results.Conflict(new
        {
            code = "budget_not_configured",
            message = "Set a trip budget before evaluating budget alerts."
        });

        var trip = await db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tripId, ct);
        if (trip is null) return Results.NotFound();

        var expenses = await db.TripExpenses.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .Select(x => new ExpenseInput(x.Id, x.Amount, x.CurrencyCode, x.ExpenseDate))
            .ToListAsync(ct);

        var snapshots = await db.TripExpenseConversions.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ConvertedCurrencyCode == budget.CurrencyCode)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ConversionInput(x.ExpenseId, x.OriginalAmount,
                x.OriginalCurrencyCode, x.ConvertedAmount, x.ExchangeRate,
                x.ExpenseDate, x.RateDate))
            .ToListAsync(ct);
        var latest = snapshots.GroupBy(x => x.ExpenseId)
            .ToDictionary(x => x.Key, x => x.First());

        decimal actual = 0m;
        var unconvertedCount = 0;
        foreach (var expense in expenses)
        {
            if (expense.CurrencyCode == budget.CurrencyCode)
            {
                actual += expense.Amount;
                continue;
            }
            if (latest.TryGetValue(expense.Id, out var snapshot) &&
                snapshot.OriginalAmount == expense.Amount &&
                snapshot.OriginalCurrencyCode == expense.CurrencyCode &&
                snapshot.ExpenseDate == expense.ExpenseDate &&
                snapshot.RateDate <= expense.ExpenseDate &&
                snapshot.ExchangeRate > 0m)
            {
                actual += snapshot.ConvertedAmount;
            }
            else unconvertedCount++;
        }

        if (budget.Amount <= 0m) return Results.Conflict(new
        {
            code = "invalid_budget_amount",
            message = "A positive budget amount is required to evaluate percentage thresholds."
        });

        if (unconvertedCount > 0) return Results.Ok(new BudgetAlertEvaluationResponse(
            tripId, budget.Id, budget.CurrencyCode, budget.Amount, actual,
            null, "partial_data", unconvertedCount, 0,
            "Alerts were not evaluated because some expenses have no matching saved conversion. Convert them first to avoid false threshold results."));

        var elapsedDays = Math.Max(0, Math.Min(
            DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - trip.StartDate.DayNumber + 1,
            trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1));
        var totalDays = Math.Max(1, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1);
        decimal? projected = elapsedDays > 0 && expenses.Count > 0
            ? actual / elapsedDays * totalDays : null;

        var created = 0;
        foreach (var threshold in Thresholds.Where(value => actual >= budget.Amount * value / 100m))
        {
            var message = threshold == 100
                ? $"Trip spending has reached or exceeded 100% of the {budget.CurrencyCode} budget."
                : $"Trip spending has reached {threshold}% of the {budget.CurrencyCode} budget.";
            created += await InsertAlertAsync(db, tripId, budget.Id, "threshold",
                threshold, budget.Amount, budget.CurrencyCode, actual, projected, message, ct);
        }

        if (projected.HasValue && projected.Value > budget.Amount && actual <= budget.Amount)
        {
            var message = $"At the current average daily spend, the trip is projected to exceed its {budget.CurrencyCode} budget.";
            created += await InsertAlertAsync(db, tripId, budget.Id, "projected_over_budget",
                0, budget.Amount, budget.CurrencyCode, actual, projected, message, ct);
        }

        var status = actual >= budget.Amount ? "over_budget"
            : projected.HasValue && projected.Value > budget.Amount ? "projected_over_budget"
            : "on_track";
        return Results.Ok(new BudgetAlertEvaluationResponse(
            tripId, budget.Id, budget.CurrencyCode, budget.Amount, actual,
            projected, status, 0, created,
            created > 0 ? "New budget alerts were recorded for all trip members."
                : "No new alerts were needed. Previously recorded thresholds are not duplicated."));
    }

    private static async Task<int> InsertAlertAsync(
        TravelOsDbContext db, Guid tripId, Guid budgetId, string type,
        int threshold, decimal budgetAmount, string currency, decimal actual,
        decimal? projected, string message, CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""TripBudgetAlerts""
              (""Id"", ""TripId"", ""BudgetId"", ""AlertType"", ""ThresholdPercent"",
               ""BudgetAmountSnapshot"", ""CurrencyCode"", ""ActualAmountSnapshot"",
               ""ProjectedAmountSnapshot"", ""Message"", ""TriggeredAt"")
            VALUES
              (gen_random_uuid(), {tripId}, {budgetId}, {type}, {threshold},
               {budgetAmount}, {currency}, {actual}, {projected}, {message}, now())
            ON CONFLICT (""TripId"", ""BudgetId"", ""AlertType"", ""ThresholdPercent"",
                         ""BudgetAmountSnapshot"", ""CurrencyCode"")
            DO NOTHING", ct);
    }

    private sealed record ExpenseInput(Guid Id, decimal Amount, string CurrencyCode, DateOnly ExpenseDate);
    private sealed record ConversionInput(Guid ExpenseId, decimal OriginalAmount,
        string OriginalCurrencyCode, decimal ConvertedAmount, decimal ExchangeRate,
        DateOnly ExpenseDate, DateOnly RateDate);
}

public sealed record BudgetAlertEvaluationResponse(
    Guid TripId, Guid BudgetId, string CurrencyCode, decimal BudgetAmount,
    decimal ActualAmount, decimal? ProjectedAmount, string Status,
    int UnconvertedExpenseCount, int NewAlertsCreated, string Message);
