using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Assistant;

public static class TripAssistantEndpoints
{
    public static IEndpointRouteBuilder MapTripAssistantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/assistant")
            .WithTags("Travel Assistant")
            .RequireAuthorization();

        group.MapGet("/briefing", GetBriefingAsync).WithName("GetTripAssistantBriefing");
        group.MapPost("/ask", AskAsync).WithName("AskTripAssistant");
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<TripContext?> LoadContextAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == tripId && (x.OwnerUserId == userId ||
                db.TripMembers.Any(member => member.TripId == x.Id && member.UserId == userId)), ct);
        if (trip is null) return null;

        var itinerary = await db.Itineraries.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .Select(x => new ItineraryContext(
                x.Days.OrderBy(day => day.DayNumber).Select(day => new DayContext(
                    day.DayNumber, day.Date,
                    day.Activities.OrderBy(activity => activity.SortOrder).Select(activity =>
                        new ActivityContext(activity.Title, activity.StartTime, activity.EndTime,
                            activity.EstimatedCost, activity.CurrencyCode)).ToList()
                )).ToList()))
            .FirstOrDefaultAsync(ct);

        var budget = await db.Budgets.AsNoTracking().FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        var expenses = await db.TripExpenses.AsNoTracking()
            .Where(x => x.TripId == tripId).Select(x => new ExpenseContext(x.Amount, x.CurrencyCode)).ToListAsync(ct);
        var weather = await db.TripWeatherAssessments.AsNoTracking()
            .Where(x => x.TripId == tripId && x.ActivityDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderByDescending(x => x.ForecastFetchedAt).Take(100)
            .Select(x => new WeatherContext(x.ActivityTitle, x.ActivityDate, x.Severity,
                x.Summary, x.Recommendation, x.ForecastFetchedAt)).ToListAsync(ct);
        var flights = await db.TripFlights.AsNoTracking().Where(x => x.TripId == tripId)
            .OrderBy(x => x.ScheduledDepartureUtc)
            .Select(x => new FlightContext(x.FlightIdentifier, x.OriginAirportCode,
                x.DestinationAirportCode, x.Status, x.ProviderStatus,
                x.DepartureDelayMinutes, x.ScheduledDepartureUtc, x.EstimatedDepartureUtc,
                x.ActualDepartureUtc)).ToListAsync(ct);
        var budgetAlerts = await db.TripBudgetAlerts.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.TriggeredAt).Take(10)
            .Select(x => new BudgetAlertContext(x.AlertType, x.ThresholdPercent,
                x.Message, x.CurrencyCode, x.TriggeredAt)).ToListAsync(ct);
        var notifications = await db.TripNotifications.AsNoTracking()
            .Where(x => x.TripId == tripId && x.UserId == userId && x.DismissedAt == null)
            .OrderByDescending(x => x.CreatedAt).Take(20)
            .Select(x => new NotificationContext(x.Title, x.Message, x.ScheduledForUtc, x.ReadAt)).ToListAsync(ct);

        return new TripContext(trip, itinerary, budget is null ? null : new BudgetContext(budget.Amount, budget.CurrencyCode),
            expenses, weather, flights, notifications, budgetAlerts);
    }

    private static async Task<IResult> GetBriefingAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var context = await LoadContextAsync(tripId, userId, db, ct);
        if (context is null) return Results.NotFound();
        return Results.Ok(BuildBriefing(context));
    }

    private static async Task<IResult> AskAsync(
        Guid tripId, AskTripAssistantRequest request, ClaimsPrincipal principal,
        TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var question = request.Question?.Trim() ?? "";
        if (question.Length is < 2 or > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["question"] = ["Enter a question between 2 and 500 characters."] });

        var context = await LoadContextAsync(tripId, userId, db, ct);
        if (context is null) return Results.NotFound();
        return Results.Ok(BuildAnswer(context, question));
    }

    private static TripAssistantBriefingResponse BuildBriefing(TripContext context)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysUntilStart = context.Trip.StartDate.DayNumber - now.DayNumber;
        var daysRemaining = Math.Max(0, context.Trip.EndDate.DayNumber - Math.Max(now.DayNumber, context.Trip.StartDate.DayNumber) + 1);
        var insights = new List<TripAssistantInsight>();

        if (context.Flights.Count == 0)
            insights.Add(new("flights", "No flights saved", "Add your flight details to keep journey timings together.", "info"));
        else
        {
            foreach (var flight in context.Flights.Where(x =>
                (x.DepartureDelayMinutes ?? 0) >= 30 ||
                (x.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase)) ||
                (x.ProviderStatus?.Contains("cancel", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                var delay = flight.DepartureDelayMinutes is > 0 ? $" Reported departure delay: {flight.DepartureDelayMinutes} minutes." : "";
                insights.Add(new("flight", $"Check flight {flight.Identifier}",
                    $"{flight.Origin} → {flight.Destination}. Status: {flight.ProviderStatus ?? flight.Status}.{delay}",
                    "warning"));
            }
        }

        var latestWeather = context.Weather.GroupBy(x => (x.ActivityTitle, x.ActivityDate))
            .Select(group => group.OrderByDescending(x => x.FetchedAt).First()).ToList();
        foreach (var item in latestWeather.Where(x =>
            x.Severity.Equals("high", StringComparison.OrdinalIgnoreCase) ||
            x.Severity.Equals("moderate", StringComparison.OrdinalIgnoreCase)))
            insights.Add(new("weather", $"Weather check: {item.ActivityTitle}",
                $"{item.ActivityDate:yyyy-MM-dd}. {item.Summary} {item.Recommendation}".Trim(), 
                item.Severity.Equals("high", StringComparison.OrdinalIgnoreCase) ? "warning" : "caution"));

        if (context.Itinerary is null || context.Itinerary.Days.Count == 0)
            insights.Add(new("itinerary", "No itinerary saved",
                "This trip does not have any saved itinerary days yet. Add days and activities to build your plan.", "info"));
        else
        {
            var emptyUpcomingDays = context.Itinerary.Days.Count(day =>
                day.Date >= now && day.Activities.Count == 0);
            if (emptyUpcomingDays > 0)
                insights.Add(new("itinerary", "Upcoming days need a plan",
                    $"{emptyUpcomingDays} upcoming itinerary day(s) have no saved activities.", "info"));
            var untimed = context.Itinerary.Days.Where(day => day.Date >= now)
                .SelectMany(day => day.Activities)
                .Count(activity => !activity.StartTime.HasValue || !activity.EndTime.HasValue);
            if (untimed > 0)
                insights.Add(new("schedule", "Some activities are not timed",
                    $"{untimed} upcoming activity/activities are missing a start or end time and cannot be checked for overlaps.", "info"));
            foreach (var day in context.Itinerary.Days)
            {
                var timed = day.Activities.Where(x => x.StartTime.HasValue && x.EndTime.HasValue)
                    .OrderBy(x => x.StartTime).ToList();
                for (var i = 1; i < timed.Count; i++)
                    if (timed[i].StartTime!.Value < timed[i - 1].EndTime!.Value)
                        insights.Add(new("schedule", $"Possible overlap on day {day.DayNumber}",
                            $"{timed[i - 1].Title} and {timed[i].Title} have overlapping saved times.", "warning"));
            }
        }

        if (context.Budget is not null)
        {
            var spent = context.Expenses.Where(x =>
                x.CurrencyCode.Equals(context.Budget.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);
            var hasOtherCurrencies = context.Expenses.Any(x =>
                !x.CurrencyCode.Equals(context.Budget.CurrencyCode, StringComparison.OrdinalIgnoreCase));
            var percent = context.Budget.Amount > 0 ? spent / context.Budget.Amount * 100m : 0m;
            if (context.Budget.Amount > 0 && percent >= 80)
                insights.Add(new("budget", "Budget getting tight",
                    $"Recorded expenses in {context.Budget.CurrencyCode}: {spent:N2} of {context.Budget.Amount:N2} ({percent:N0}%).",
                    percent >= 100 ? "warning" : "caution"));
            if (hasOtherCurrencies)
                insights.Add(new("budget", "Mixed expense currencies",
                    "Some expenses use a different currency from the trip budget and are excluded from this comparison.", "info"));
        }

        foreach (var alert in context.BudgetAlerts.Take(5))
            insights.Add(new("budget", "Saved budget alert", alert.Message, "caution"));

        var dueReminders = context.Notifications.Count(x =>
            x.ScheduledForUtc.HasValue && x.ScheduledForUtc.Value <= DateTimeOffset.UtcNow && x.ReadAt is null);
        if (dueReminders > 0)
            insights.Add(new("reminders", "Unread trip reminders", $"You have {dueReminders} due, unread reminder(s).", "info"));

        if (insights.Count == 0)
            insights.Add(new("overview", "Looking good so far",
                "No high-priority issues were found in the trip information currently saved. Check live flight and weather data before travelling.", "success"));

        return new(context.Trip.Id, context.Trip.Name, context.Trip.Destination,
            context.Trip.StartDate, context.Trip.EndDate, daysUntilStart, daysRemaining,
            insights, DateTimeOffset.UtcNow,
            "This briefing uses information saved in TravelOS. Weather and flight details may be out of date; verify with the official provider before acting.");
    }

    private static TripAssistantAnswerResponse BuildAnswer(TripContext context, string question)
    {
        var q = question.ToLowerInvariant();
        string category;
        string answer;
        var facts = new List<string>();

        if (ContainsAny(q, "weather", "rain", "wind", "temperature", "forecast"))
        {
            category = "weather";
            var latest = context.Weather.GroupBy(x => (x.ActivityTitle, x.ActivityDate))
                .Select(group => group.OrderByDescending(x => x.FetchedAt).First())
                .OrderByDescending(x => x.Severity.Equals("high", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (latest.Count == 0) answer = "I don't have saved weather assessments for this trip yet. Run the trip weather scan first, then ask me again.";
            else
            {
                foreach (var item in latest.Take(5))
                    facts.Add($"{item.ActivityDate:yyyy-MM-dd} · {item.ActivityTitle}: {item.Severity}. {item.Summary} {item.Recommendation}".Trim());
                answer = $"I found {latest.Count} saved activity weather assessment(s). Review the latest conditions below and recheck the forecast close to your activity date.";
            }
        }
        else if (ContainsAny(q, "flight", "airport", "delay", "departure", "arrival", "connection"))
        {
            category = "flights";
            if (context.Flights.Count == 0) answer = "No flights are saved for this trip yet. Add your flight details to review departure times and disruption signals.";
            else
            {
                foreach (var flight in context.Flights.Take(8))
                    facts.Add($"{flight.Identifier}: {flight.Origin} → {flight.Destination}; {flight.Status}; provider: {flight.ProviderStatus ?? "not available"}; delay: {(flight.DepartureDelayMinutes.HasValue ? flight.DepartureDelayMinutes.Value + " min" : "not reported")}.");
                answer = "Here are the flight details currently saved in TravelOS. A missing status does not mean a flight is on time; refresh it and confirm with the airline.";
            }
        }
        else if (ContainsAny(q, "budget", "money", "cost", "spend", "expense", "expensive"))
        {
            category = "budget";
            if (context.Budget is null) answer = "No trip budget has been configured yet. Add a budget to track your planned amount.";
            else
            {
                var matching = context.Expenses.Where(x => x.CurrencyCode.Equals(context.Budget.CurrencyCode, StringComparison.OrdinalIgnoreCase)).ToList();
                var spent = matching.Sum(x => x.Amount);
                facts.Add($"Trip budget: {context.Budget.Amount:N2} {context.Budget.CurrencyCode}.");
                facts.Add($"Recorded expenses in the same currency: {spent:N2} {context.Budget.CurrencyCode}.");
                facts.AddRange(context.BudgetAlerts.Take(5).Select(x => $"Budget alert ({x.ThresholdPercent}%): {x.Message}"));
                var other = context.Expenses.Where(x => !x.CurrencyCode.Equals(context.Budget.CurrencyCode, StringComparison.OrdinalIgnoreCase)).ToList();
                if (other.Count > 0) facts.Add($"{other.Count} expense(s) in other currencies were excluded; no conversion was assumed.");
                answer = context.Budget.Amount > 0
                    ? $"Recorded expenses are {(spent / context.Budget.Amount * 100m):N0}% of your trip budget, based only on expenses in the budget currency. This is a recorded-expense comparison, not a prediction of final spend."
                    : "Your trip budget is zero, so a percentage comparison is not meaningful.";
            }
        }
        else if (ContainsAny(q, "schedule", "itinerary", "activity", "time", "plan", "overlap"))
        {
            category = "schedule";
            if (context.Itinerary is null || context.Itinerary.Days.Count == 0)
                answer = "There isn't an itinerary saved for this trip yet. Add itinerary days and activities so I can review your plan.";
            else
            {
                var overlaps = new List<string>();
                var unscheduled = 0;
                foreach (var day in context.Itinerary.Days)
                {
                    var timed = day.Activities.Where(x => x.StartTime.HasValue && x.EndTime.HasValue).OrderBy(x => x.StartTime).ToList();
                    unscheduled += day.Activities.Count - timed.Count;
                    for (var i = 1; i < timed.Count; i++)
                        if (timed[i].StartTime!.Value < timed[i - 1].EndTime!.Value)
                            overlaps.Add($"Day {day.DayNumber}: {timed[i - 1].Title} overlaps {timed[i].Title}.");
                }
                facts.Add($"{context.Itinerary.Days.Sum(x => x.Activities.Count)} saved activities across {context.Itinerary.Days.Count} itinerary day(s).");
                facts.Add($"{unscheduled} activity/activities have no complete start and end time.");
                facts.AddRange(overlaps.Take(5));
                answer = overlaps.Count > 0
                    ? $"I found {overlaps.Count} possible schedule overlap(s). Review the times before approving your itinerary."
                    : "I didn't find overlaps among activities with both start and end times. Activities without complete times cannot be checked.";
            }
        }
        else if (ContainsAny(q, "reminder", "notification", "remember"))
        {
            category = "reminders";
            var reminders = context.Notifications.Where(x => x.ReadAt is null)
                .OrderBy(x => x.ScheduledForUtc ?? DateTimeOffset.MinValue).Take(8).ToList();
            foreach (var item in reminders)
                facts.Add($"{item.Title}: {item.Message}" + (item.ScheduledForUtc.HasValue ? $" · {item.ScheduledForUtc.Value:yyyy-MM-dd HH:mm} UTC" : ""));
            answer = reminders.Count == 0 ? "There are no unread trip notifications saved for you." : $"You have {reminders.Count} unread trip notification(s) in your inbox.";
        }
        else
        {
            category = "overview";
            var briefing = BuildBriefing(context);
            answer = $"You're asking about {context.Trip.Name} in {context.Trip.Destination}. Your trip runs from {context.Trip.StartDate:yyyy-MM-dd} to {context.Trip.EndDate:yyyy-MM-dd}. I can help review your itinerary, weather, flights, budget and reminders.";
            facts.AddRange(briefing.Insights.Take(5).Select(x => $"{x.Severity.ToUpperInvariant()}: {x.Title} — {x.Message}"));
        }

        return new(context.Trip.Id, question, category, answer, facts,
            ["What should I check before travelling?", "Are there any schedule conflicts?", "How is my budget looking?", "Are there any flight or weather issues?"],
            DateTimeOffset.UtcNow,
            "Answers are generated from trip data currently saved in TravelOS, not from a live web search or an external AI model. Verify changing flight and weather details with official providers.");
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);

    public sealed record AskTripAssistantRequest(string Question);
    public sealed record TripAssistantInsight(string Category, string Title, string Message, string Severity);
    public sealed record TripAssistantBriefingResponse(Guid TripId, string TripName, string Destination,
        DateOnly StartDate, DateOnly EndDate, int DaysUntilStart, int DaysRemaining,
        IReadOnlyList<TripAssistantInsight> Insights, DateTimeOffset GeneratedAtUtc, string Disclaimer);
    public sealed record TripAssistantAnswerResponse(Guid TripId, string Question, string Category,
        string Answer, IReadOnlyList<string> Facts, IReadOnlyList<string> SuggestedQuestions,
        DateTimeOffset AnsweredAtUtc, string Disclaimer);

    private sealed record TripContext(Trip Trip, ItineraryContext? Itinerary, BudgetContext? Budget,
        List<ExpenseContext> Expenses, List<WeatherContext> Weather, List<FlightContext> Flights,
        List<NotificationContext> Notifications, List<BudgetAlertContext> BudgetAlerts);
    private sealed record ItineraryContext(List<DayContext> Days);
    private sealed record DayContext(int DayNumber, DateOnly Date, List<ActivityContext> Activities);
    private sealed record ActivityContext(string Title, TimeOnly? StartTime, TimeOnly? EndTime,
        decimal? EstimatedCost, string? CurrencyCode);
    private sealed record BudgetContext(decimal Amount, string CurrencyCode);
    private sealed record ExpenseContext(decimal Amount, string CurrencyCode);
    private sealed record WeatherContext(string ActivityTitle, DateOnly ActivityDate, string Severity,
        string Summary, string Recommendation, DateTimeOffset FetchedAt);
    private sealed record FlightContext(string Identifier, string Origin, string Destination, string Status,
        string? ProviderStatus, int? DepartureDelayMinutes, DateTimeOffset ScheduledDepartureUtc,
        DateTimeOffset? EstimatedDepartureUtc, DateTimeOffset? ActualDepartureUtc);
    private sealed record NotificationContext(string Title, string Message, DateTimeOffset? ScheduledForUtc, DateTimeOffset? ReadAt);
    private sealed record BudgetAlertContext(string AlertType, int ThresholdPercent, string Message,
        string CurrencyCode, DateTimeOffset TriggeredAt);
}
