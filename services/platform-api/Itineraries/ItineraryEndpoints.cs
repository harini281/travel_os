using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Itineraries;

public static class ItineraryEndpoints
{
    public static IEndpointRouteBuilder MapItineraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/itinerary")
            .WithTags("Itineraries")
            .RequireAuthorization();

        group.MapGet("/", GetAsync).WithName("GetTripItinerary");
        group.MapPost("/", CreateAsync).WithName("CreateTripItinerary");
        group.MapPost("/days", AddDayAsync).WithName("AddItineraryDay");
        group.MapPost("/days/{dayId:guid}/activities", AddActivityAsync).WithName("AddItineraryActivity");
        group.MapPut("/activities/{activityId:guid}", UpdateActivityAsync).WithName("UpdateItineraryActivity");
        group.MapDelete("/activities/{activityId:guid}", DeleteActivityAsync).WithName("DeleteItineraryActivity");
        group.MapPost("/approve", ApproveAsync).WithName("ApproveItinerary");
        group.MapPost("/drafts", GenerateDraftAsync).WithName("GenerateAiItineraryDraft");
        group.MapGet("/drafts/{draftId:guid}", GetDraftAsync).WithName("GetAiItineraryDraft");
        group.MapPost("/drafts/{draftId:guid}/apply", ApplyDraftAsync).WithName("ApplyAiItineraryDraft");

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<Trip?> FindTripForMemberAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct)
    {
        return await db.Trips.AsNoTracking().FirstOrDefaultAsync(
            trip => trip.Id == tripId &&
                (trip.OwnerUserId == userId ||
                 db.TripMembers.Any(member => member.TripId == trip.Id && member.UserId == userId)),
            ct);
    }

    private static async Task<Trip?> FindTripForOwnerAsync(
        Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct)
    {
        return await db.Trips.FirstOrDefaultAsync(
            trip => trip.Id == tripId && trip.OwnerUserId == userId, ct);
    }

    private static ItineraryResponse ToResponse(Itinerary itinerary)
    {
        var days = itinerary.Days
            .OrderBy(day => day.DayNumber)
            .Select(day => new ItineraryDayResponse(
                day.Id,
                day.DayNumber,
                day.Date,
                day.Activities.OrderBy(activity => activity.SortOrder)
                    .Select(activity => new ActivityResponse(
                        activity.Id, activity.Title, activity.Description,
                        activity.PlaceName, activity.Latitude, activity.Longitude,
                        activity.DurationMinutes, activity.EstimatedCost,
                        activity.CurrencyCode, activity.SortOrder))
                    .ToArray()))
            .ToArray();

        return new ItineraryResponse(
            itinerary.Id, itinerary.TripId, itinerary.Version,
            itinerary.ApprovedAt is not null, itinerary.ApprovedAt, days);
    }

    private static IResult? ValidateActivity(ActivityRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 180)
            errors["title"] = ["Activity title is required and must be at most 180 characters."];

        if (request.Description?.Length > 2000)
            errors["description"] = ["Description must be at most 2,000 characters."];

        if (request.PlaceName?.Length > 200)
            errors["placeName"] = ["Place name must be at most 200 characters."];

        if (request.Latitude.HasValue != request.Longitude.HasValue)
            errors["coordinates"] = ["Latitude and longitude must be supplied together."];

        if (request.Latitude is < -90 or > 90)
            errors["latitude"] = ["Latitude must be between -90 and 90."];

        if (request.Longitude is < -180 or > 180)
            errors["longitude"] = ["Longitude must be between -180 and 180."];

        if (request.DurationMinutes is <= 0)
            errors["durationMinutes"] = ["Duration must be greater than zero."];

        if (request.SortOrder < 0)
            errors["sortOrder"] = ["Sort order cannot be negative."];

        if (request.EstimatedCost is < 0)
            errors["estimatedCost"] = ["Estimated cost cannot be negative."];

        if (request.CurrencyCode is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(request.CurrencyCode, "^[A-Za-z]{3}$"))
            errors["currencyCode"] = ["Currency code must contain three letters."];

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static async Task<IResult> GetAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForMemberAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries.AsNoTracking()
            .Include(x => x.Days)
            .ThenInclude(x => x.Activities)
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        return itinerary is null
            ? Results.NoContent()
            : Results.Ok(ToResponse(itinerary));
    }

    private static async Task<IResult> CreateAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var existing = await db.Itineraries.AnyAsync(x => x.TripId == tripId, ct);
        if (existing)
            return Results.Conflict(new { message = "An itinerary already exists for this trip." });

        var itinerary = new Itinerary { TripId = tripId, Version = 1 };
        db.Itineraries.Add(itinerary);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary",
            ToResponse(itinerary));
    }

    private static async Task<IResult> AddDayAsync(
        Guid tripId,
        AddItineraryDayRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        if (request.DayNumber < 1)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dayNumber"] = ["Day number must be at least 1."]
            });

        if (request.Date < trip.StartDate || request.Date > trip.EndDate)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["date"] = ["Day date must fall within the trip date range."]
            });

        var expectedDayNumber = request.Date.DayNumber - trip.StartDate.DayNumber + 1;
        if (request.DayNumber != expectedDayNumber)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dayNumber"] = ["Day number must match the date's position in the trip."]
            });

        var duplicate = await db.ItineraryDays.AnyAsync(
            x => x.ItineraryId == itinerary.Id &&
                (x.DayNumber == request.DayNumber || x.Date == request.Date), ct);
        if (duplicate)
            return Results.Conflict(new { message = "A day already exists for this date." });

        var day = new ItineraryDay
        {
            ItineraryId = itinerary.Id,
            DayNumber = request.DayNumber,
            Date = request.Date
        };
        db.ItineraryDays.Add(day);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary/days/{day.Id}",
            new ItineraryDayResponse(day.Id, day.DayNumber, day.Date, []));
    }

    private static async Task<IResult> AddActivityAsync(
        Guid tripId,
        Guid dayId,
        ActivityRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateActivity(request);
        if (invalid is not null) return invalid;

        var day = await db.ItineraryDays
            .Include(x => x.Activities)
            .FirstOrDefaultAsync(x => x.Id == dayId, ct);
        if (day is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.Id == day.ItineraryId && x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        var activity = new Activity
        {
            ItineraryDayId = dayId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            PlaceName = request.PlaceName?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DurationMinutes = request.DurationMinutes,
            EstimatedCost = request.EstimatedCost,
            CurrencyCode = request.EstimatedCost.HasValue
                ? (request.CurrencyCode?.ToUpperInvariant() ?? trip.CurrencyCode)
                : request.CurrencyCode?.ToUpperInvariant(),
            SortOrder = request.SortOrder > 0
                ? request.SortOrder
                : day.Activities.Select(x => x.SortOrder).DefaultIfEmpty(0).Max() + 1
        };

        db.Activities.Add(activity);
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary/days/{dayId}/activities/{activity.Id}",
            new ActivityResponse(
                activity.Id, activity.Title, activity.Description, activity.PlaceName,
                activity.Latitude, activity.Longitude, activity.DurationMinutes,
                activity.EstimatedCost, activity.CurrencyCode, activity.SortOrder));
    }

    private static async Task<IResult> UpdateActivityAsync(
        Guid tripId,
        Guid activityId,
        ActivityRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var invalid = ValidateActivity(request);
        if (invalid is not null) return invalid;

        var activity = await db.Activities.FirstOrDefaultAsync(
            x => x.Id == activityId, ct);
        if (activity is null) return Results.NotFound();

        var day = await db.ItineraryDays.FirstOrDefaultAsync(
            x => x.Id == activity.ItineraryDayId, ct);
        if (day is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.Id == day.ItineraryId && x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        activity.Title = request.Title.Trim();
        activity.Description = request.Description?.Trim();
        activity.PlaceName = request.PlaceName?.Trim();
        activity.Latitude = request.Latitude;
        activity.Longitude = request.Longitude;
        activity.DurationMinutes = request.DurationMinutes;
        activity.EstimatedCost = request.EstimatedCost;
        activity.CurrencyCode = request.EstimatedCost.HasValue
            ? (request.CurrencyCode?.ToUpperInvariant() ?? trip.CurrencyCode)
            : request.CurrencyCode?.ToUpperInvariant();
        activity.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new ActivityResponse(
            activity.Id, activity.Title, activity.Description, activity.PlaceName,
            activity.Latitude, activity.Longitude, activity.DurationMinutes,
            activity.EstimatedCost, activity.CurrencyCode, activity.SortOrder));
    }

    private static async Task<IResult> DeleteActivityAsync(
        Guid tripId,
        Guid activityId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var activity = await db.Activities.FirstOrDefaultAsync(
            x => x.Id == activityId, ct);
        if (activity is null) return Results.NotFound();

        var day = await db.ItineraryDays.FirstOrDefaultAsync(
            x => x.Id == activity.ItineraryDayId, ct);
        if (day is null) return Results.NotFound();

        var itinerary = await db.Itineraries.FirstOrDefaultAsync(
            x => x.Id == day.ItineraryId && x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "Approved itineraries are read-only." });

        db.Activities.Remove(activity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ApproveAsync(
        Guid tripId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var itinerary = await db.Itineraries
            .Include(x => x.Days)
            .ThenInclude(x => x.Activities)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(x => x.TripId == tripId, ct);
        if (itinerary is null) return Results.NotFound();
        if (itinerary.ApprovedAt is not null)
            return Results.Conflict(new { message = "This itinerary is already approved." });

        if (itinerary.Days.Count == 0 || itinerary.Days.All(day => day.Activities.Count == 0))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["itinerary"] = ["Add at least one day and one activity before approving."]
            });

        itinerary.ApprovedByUserId = userId;
        itinerary.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(itinerary));
    }

    private static string? ValidateDraftPayload(AiDraftItinerary payload, Trip trip)
    {
        if (string.IsNullOrWhiteSpace(payload.Title) || payload.Title.Length > 160)
            return "The AI provider returned an invalid itinerary title.";
        if (string.IsNullOrWhiteSpace(payload.Summary) || payload.Summary.Length > 1000)
            return "The AI provider returned an invalid itinerary summary.";

        var expectedDays = trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1;
        if (payload.Days.Count != expectedDays)
            return "The AI provider returned an incomplete set of trip days.";

        var days = payload.Days.OrderBy(day => day.DayNumber).ToArray();
        for (var index = 0; index < days.Length; index++)
        {
            var day = days[index];
            if (day.DayNumber != index + 1 || day.Date != trip.StartDate.AddDays(index))
                return "The AI provider returned inconsistent itinerary dates.";
            if (day.Activities.Count is < 1 or > 8)
                return "Each generated day must contain between one and eight activities.";

            foreach (var activity in day.Activities)
            {
                if (string.IsNullOrWhiteSpace(activity.Title) || activity.Title.Length > 180)
                    return "The AI provider returned an invalid activity title.";
                if (activity.Description?.Length > 2000 || activity.PlaceName?.Length > 200)
                    return "The AI provider returned activity text that is too long.";
                if (activity.Latitude.HasValue != activity.Longitude.HasValue ||
                    activity.Latitude is < -90 or > 90 ||
                    activity.Longitude is < -180 or > 180)
                    return "The AI provider returned invalid coordinates.";
                if (activity.DurationMinutes is <= 0 || activity.EstimatedCost is < 0 || activity.SortOrder < 0)
                    return "The AI provider returned invalid activity duration, cost or order.";
                if (activity.CurrencyCode is not null &&
                    !string.Equals(activity.CurrencyCode, trip.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                    return "The AI provider returned an activity cost in a different currency from the trip.";
            }
        }

        return null;
    }

    private static async Task<IResult> GenerateDraftAsync(
        Guid tripId,
        GenerateAiDraftRequest request,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var interests = request.Interests ?? Array.Empty<string>();
        var errors = new Dictionary<string, string[]>();
        if (request.TravellerCount is < 1 or > 30)
            errors["travellerCount"] = ["Traveller count must be between 1 and 30."];
        if (request.BudgetAmount is < 0)
            errors["budgetAmount"] = ["Budget cannot be negative."];
        if (interests.Count > 20 || interests.Any(value => value.Length > 60))
            errors["interests"] = ["Provide at most 20 interests, each no longer than 60 characters."];
        if (request.TravelPace is not ("relaxed" or "balanced" or "fast"))
            errors["travelPace"] = ["Travel pace must be relaxed, balanced or fast."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var baseUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:8001";
        var client = httpClientFactory.CreateClient("ai-planner");
        try
        {
            client.BaseAddress = new Uri(baseUrl);
            var aiRequest = new
            {
                contractVersion = "1.0",
                destination = trip.Destination,
                startDate = trip.StartDate,
                endDate = trip.EndDate,
                travellerCount = request.TravellerCount,
                budgetAmount = request.BudgetAmount,
                currency = trip.CurrencyCode,
                interests,
                travelPace = request.TravelPace
            };

            using var response = await client.PostAsJsonAsync(
                "/api/v1/planner/drafts", aiRequest, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 503)
                    return Results.Problem(
                        "The AI provider is not configured. No draft was saved.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                return Results.Problem(
                    "The AI service could not generate a valid itinerary draft.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var generated = await response.Content.ReadFromJsonAsync<AiPlannerResponse>(
                cancellationToken: ct);
            if (generated is null || generated.Status != "generated" ||
                generated.Approved || generated.Itinerary is null)
                return Results.Problem(
                    "The AI service is in demo mode or returned no generated itinerary. No draft was saved.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var validationError = ValidateDraftPayload(generated.Itinerary, trip);
            if (validationError is not null)
                return Results.Problem(validationError, statusCode: StatusCodes.Status502BadGateway);

            var draft = new GeneratedItineraryDraft
            {
                TripId = tripId,
                RequestedByUserId = userId,
                Status = "pending_review",
                ContractVersion = "1.0",
                PayloadJson = JsonSerializer.Serialize(generated, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.GeneratedItineraryDrafts.Add(draft);
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/v1/trips/{tripId}/itinerary/drafts/{draft.Id}",
                new GeneratedDraftResponse(
                    draft.Id, tripId, draft.Status, draft.CreatedAt,
                    generated.Provider, generated.Model,
                    generated.Warnings ?? Array.Empty<string>(), generated.Itinerary));
        }
        catch (UriFormatException)
        {
            return Results.Problem("The AI service base URL is invalid.", statusCode: 500);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The AI service is unavailable. No draft was saved.", statusCode: 503);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem("The AI service timed out. Please try again.", statusCode: 504);
        }
        catch (JsonException)
        {
            return Results.Problem("The AI service returned an unreadable response.", statusCode: 502);
        }
    }

    private static async Task<IResult> GetDraftAsync(
        Guid tripId,
        Guid draftId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForMemberAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var draft = await db.GeneratedItineraryDrafts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == draftId && x.TripId == tripId, ct);
        if (draft is null) return Results.NotFound();

        using var payload = JsonDocument.Parse(draft.PayloadJson);
        return Results.Ok(new
        {
            draft.Id,
            draft.TripId,
            draft.Status,
            draft.CreatedAt,
            payload = payload.RootElement
        });
    }

    private static async Task<IResult> ApplyDraftAsync(
        Guid tripId,
        Guid draftId,
        ClaimsPrincipal principal,
        TravelOsDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var trip = await FindTripForOwnerAsync(tripId, userId, db, ct);
        if (trip is null) return Results.NotFound();

        var draft = await db.GeneratedItineraryDrafts.FirstOrDefaultAsync(
            x => x.Id == draftId && x.TripId == tripId, ct);
        if (draft is null) return Results.NotFound();
        if (draft.Status != "pending_review")
            return Results.Conflict(new { message = "Only a pending review draft can be applied." });

        AiPlannerResponse? generated;
        try
        {
            generated = JsonSerializer.Deserialize<AiPlannerResponse>(
                draft.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.Problem("The saved AI draft could not be read.", statusCode: 500);
        }

        if (generated?.Itinerary is null || generated.Approved)
            return Results.Problem("The saved AI draft is invalid.", statusCode: 500);

        var validationError = ValidateDraftPayload(generated.Itinerary, trip);
        if (validationError is not null)
            return Results.Problem(validationError, statusCode: StatusCodes.Status422UnprocessableEntity);

        var current = await db.Itineraries
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        if (current is not null && current.ApprovedAt is null)
            return Results.Conflict(new { message = "Approve the current itinerary before applying a new AI draft." });

        var itinerary = new Itinerary
        {
            TripId = tripId,
            Version = current is null ? 1 : current.Version + 1
        };

        foreach (var draftDay in generated.Itinerary.Days.OrderBy(x => x.DayNumber))
        {
            var day = new ItineraryDay
            {
                ItineraryId = itinerary.Id,
                DayNumber = draftDay.DayNumber,
                Date = draftDay.Date
            };
            foreach (var draftActivity in draftDay.Activities.OrderBy(x => x.SortOrder))
            {
                day.Activities.Add(new Activity
                {
                    ItineraryDayId = day.Id,
                    Title = draftActivity.Title.Trim(),
                    Description = draftActivity.Description?.Trim(),
                    PlaceName = draftActivity.PlaceName?.Trim(),
                    Latitude = draftActivity.Latitude,
                    Longitude = draftActivity.Longitude,
                    DurationMinutes = draftActivity.DurationMinutes,
                    EstimatedCost = draftActivity.EstimatedCost,
                    CurrencyCode = draftActivity.CurrencyCode?.ToUpperInvariant() ?? trip.CurrencyCode,
                    SortOrder = draftActivity.SortOrder
                });
            }
            itinerary.Days.Add(day);
        }

        draft.Status = "applied";
        db.Itineraries.Add(itinerary);
        await db.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/v1/trips/{tripId}/itinerary",
            ToResponse(itinerary));
    }

}
