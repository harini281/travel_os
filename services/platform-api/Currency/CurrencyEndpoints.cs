using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Currency;

public sealed class FrankfurterExchangeRateProvider(HttpClient client)
{
    public async Task<IReadOnlyList<CurrencyOption>> GetCurrenciesAsync(CancellationToken ct)
    {
        using var response = await client.GetAsync("/v2/currencies", ct);
        if (!response.IsSuccessStatusCode)
            throw new ExchangeRateUnavailableException("The currency catalogue is temporarily unavailable.");
        var data = await response.Content.ReadFromJsonAsync<List<FrankfurterCurrencyResponse>>(cancellationToken: ct)
            ?? throw new ExchangeRateUnavailableException("The currency provider returned an empty catalogue.");
        return data.Where(x => !string.IsNullOrWhiteSpace(x.IsoNumeric) &&
                System.Text.RegularExpressions.Regex.IsMatch(x.IsoCode, "^[A-Z]{3}$"))
            .OrderBy(x => x.IsoCode)
            .Select(x => new CurrencyOption(x.IsoCode, x.Name, x.Symbol))
            .ToArray();
    }

    public async Task<ExchangeRateQuote> GetRateAsync(
        string baseCurrency, string quoteCurrency, DateOnly? date, CancellationToken ct)
    {
        var path = $"/v2/rate/{Uri.EscapeDataString(baseCurrency.ToLowerInvariant())}/{Uri.EscapeDataString(quoteCurrency.ToLowerInvariant())}";
        if (date.HasValue) path += $"?date={date.Value:yyyy-MM-dd}";
        using var response = await client.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
            throw new ExchangeRateUnavailableException("The exchange-rate provider could not return a rate for this currency pair and date.");
        var data = await response.Content.ReadFromJsonAsync<FrankfurterRateResponse>(cancellationToken: ct)
            ?? throw new ExchangeRateUnavailableException("The exchange-rate provider returned an empty response.");
        if (data.Rate <= 0 || !string.Equals(data.Base, baseCurrency, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(data.Quote, quoteCurrency, StringComparison.OrdinalIgnoreCase))
            throw new ExchangeRateUnavailableException("The exchange-rate provider returned an invalid rate.");
        return new ExchangeRateQuote(data.Base.ToUpperInvariant(), data.Quote.ToUpperInvariant(),
            data.Rate, data.Date, "Frankfurter blended reference rate");
    }

    private sealed record FrankfurterRateResponse(DateOnly Date, string Base, string Quote, decimal Rate);
    private sealed record FrankfurterCurrencyResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("iso_code")] string IsoCode,
        [property: System.Text.Json.Serialization.JsonPropertyName("iso_numeric")] string? IsoNumeric,
        string Name, string? Symbol);
}

public sealed record ExchangeRateQuote(
    string BaseCurrencyCode, string QuoteCurrencyCode, decimal Rate,
    DateOnly EffectiveDate, string Provider);

public sealed class ExchangeRateUnavailableException(string message) : Exception(message);

public static class CurrencyEndpoints
{
    public static IEndpointRouteBuilder MapCurrencyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/currency-conversions")
            .WithTags("Currency intelligence").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapGet("/currencies", ListCurrenciesAsync);
        group.MapPost("/", ConvertExpenseAsync);
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static async Task<IResult> ListCurrenciesAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db,
        FrankfurterExchangeRateProvider provider, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var member = await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!member) return Results.NotFound();
        try
        {
            return Results.Ok(await provider.GetCurrenciesAsync(ct));
        }
        catch (ExchangeRateUnavailableException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Currency catalogue unavailable", detail: ex.Message);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Currency provider unavailable",
                detail: "TravelOS could not reach the reference-rate provider.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Currency provider timed out",
                detail: "The currency catalogue request timed out. Please try again.");
        }
    }

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var member = await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!member) return Results.NotFound();
        var snapshots = await db.TripExpenseConversions.AsNoTracking()
            .Where(x => x.TripId == tripId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CurrencyConversionResponse(
                x.Id, x.ExpenseId, x.OriginalAmount, x.OriginalCurrencyCode,
                x.ConvertedAmount, x.ConvertedCurrencyCode, x.ExchangeRate,
                x.ExpenseDate, x.RateDate, x.Provider, x.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(snapshots);
    }

    private static async Task<IResult> ConvertExpenseAsync(
        Guid tripId, CreateCurrencyConversionRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db,
        FrankfurterExchangeRateProvider provider, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        var member = await db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);
        if (!member) return Results.NotFound();

        var target = request.TargetCurrencyCode?.Trim().ToUpperInvariant() ?? "";
        if (!Guid.TryParse(request.ExpenseId, out var expenseId))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["expenseId"] = ["A valid expense ID is required."]
            });
        if (!System.Text.RegularExpressions.Regex.IsMatch(target, "^[A-Z]{3}$"))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["targetCurrencyCode"] = ["Currency code must be a three-letter ISO 4217 code."]
            });

        var expense = await db.TripExpenses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == expenseId && x.TripId == tripId, ct);
        if (expense is null) return Results.NotFound();
        if (expense.ExpenseDate > DateOnly.FromDateTime(DateTime.UtcNow))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["expenseId"] = ["Historical conversion snapshots are available for expenses dated today or earlier."]
            });

        ExchangeRateQuote quote;
        if (expense.CurrencyCode == target)
        {
            quote = new ExchangeRateQuote(expense.CurrencyCode, target, 1m,
                expense.ExpenseDate, "Identity conversion");
        }
        else
        {
            try
            {
                quote = await provider.GetRateAsync(expense.CurrencyCode, target, expense.ExpenseDate, ct);
            }
            catch (ExchangeRateUnavailableException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "Exchange rate unavailable", detail: ex.Message);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "Exchange rate provider unavailable",
                    detail: "TravelOS could not reach the reference-rate provider. Your original expense has not been changed.");
            }
            catch (JsonException)
            {
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "Invalid exchange rate response",
                    detail: "The reference-rate provider returned data TravelOS could not read.");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                    title: "Exchange rate request timed out",
                    detail: "The reference-rate provider did not respond in time. Your expense has not been changed.");
            }
        }

        var converted = decimal.Round(expense.Amount * quote.Rate, 4, MidpointRounding.AwayFromZero);
        var snapshot = new TripExpenseConversion
        {
            TripId = tripId,
            ExpenseId = expense.Id,
            RequestedByUserId = userId,
            OriginalAmount = expense.Amount,
            OriginalCurrencyCode = expense.CurrencyCode,
            ConvertedAmount = converted,
            ConvertedCurrencyCode = target,
            ExchangeRate = quote.Rate,
            ExpenseDate = expense.ExpenseDate,
            RateDate = quote.EffectiveDate,
            Provider = quote.Provider
        };
        db.TripExpenseConversions.Add(snapshot);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/trips/{tripId}/currency-conversions",
            new CurrencyConversionResponse(
                snapshot.Id, snapshot.ExpenseId, snapshot.OriginalAmount,
                snapshot.OriginalCurrencyCode, snapshot.ConvertedAmount,
                snapshot.ConvertedCurrencyCode, snapshot.ExchangeRate,
                snapshot.ExpenseDate, snapshot.RateDate, snapshot.Provider, snapshot.CreatedAt));
    }
}

public sealed record CurrencyOption(string IsoCode, string Name, string? Symbol);
public sealed record CreateCurrencyConversionRequest(string ExpenseId, string TargetCurrencyCode);
public sealed record CurrencyConversionResponse(
    Guid Id, Guid ExpenseId, decimal OriginalAmount, string OriginalCurrencyCode,
    decimal ConvertedAmount, string ConvertedCurrencyCode, decimal ExchangeRate,
    DateOnly ExpenseDate, DateOnly RateDate, string Provider, DateTimeOffset CreatedAt);
