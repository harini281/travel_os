using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Receipts;

public static class ReceiptEndpoints
{
    private const long MaxFileBytes = 8 * 1024 * 1024;
    private static readonly Dictionary<string, byte[]> Signatures = new()
    {
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF],
        ["image/png"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
        ["image/webp"] = Encoding.ASCII.GetBytes("RIFF")
    };

    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/trips/{tripId:guid}/receipts")
            .WithTags("Trip receipts").RequireAuthorization().RequireRateLimiting("receipt-upload");
        group.MapGet("/", ListAsync);
        group.MapPost("/", UploadAsync).DisableAntiforgery();
        group.MapGet("/{receiptId:guid}/file", FileAsync);
        group.MapPost("/{receiptId:guid}/confirm", ConfirmAsync);
        group.MapDelete("/{receiptId:guid}", DeleteAsync);
        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static Task<bool> IsMemberAsync(Guid tripId, Guid userId, TravelOsDbContext db, CancellationToken ct) =>
        db.TripMembers.AnyAsync(x => x.TripId == tripId && x.UserId == userId, ct);

    private static string StorageRoot(IConfiguration config) =>
        Path.GetFullPath(config["Receipts:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "private-receipts"));

    private static TripReceiptResponse ToResponse(TripReceipt receipt, bool duplicate) => new(
        receipt.Id, receipt.TripId, receipt.ExpenseId, receipt.UploadedByUserId,
        receipt.OriginalFileName, receipt.ContentType, receipt.FileSizeBytes,
        receipt.Sha256, receipt.ExtractionStatus, receipt.MerchantName,
        receipt.ReceiptDate, receipt.TotalAmount, receipt.CurrencyCode,
        receipt.Confidence, receipt.ExtractionWarning, receipt.UploadedAt,
        receipt.ConfirmedAt, receipt.ConfirmedByUserId, duplicate);

    private static async Task<IResult> ListAsync(
        Guid tripId, ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var receipts = await db.TripReceipts.AsNoTracking()
            .Where(x => x.TripId == tripId).OrderByDescending(x => x.UploadedAt).ToListAsync(ct);
        var hashes = receipts.GroupBy(x => x.Sha256).ToDictionary(x => x.Key, x => x.Count());
        return Results.Ok(receipts.Select(x => ToResponse(x, hashes[x.Sha256] > 1)).ToArray());
    }

    private static async Task<IResult> UploadAsync(
        Guid tripId, [FromForm] IFormFile? file, [FromForm] Guid? expenseId,
        ClaimsPrincipal principal, TravelOsDbContext db, IHttpClientFactory clients,
        IConfiguration config, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        if (file is null || file.Length <= 0 || file.Length > MaxFileBytes)
            return Results.BadRequest(new { message = "Choose a receipt image between 1 byte and 8 MB." });
        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!Signatures.TryGetValue(contentType, out var signature))
            return Results.BadRequest(new { message = "Only JPEG, PNG and WebP receipt images are supported." });
        if (expenseId.HasValue && !await db.TripExpenses.AnyAsync(x =>
            x.Id == expenseId.Value && x.TripId == tripId, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["expenseId"] = ["The selected expense does not belong to this trip."]
            });

        await using var input = new MemoryStream();
        await file.CopyToAsync(input, ct);
        var bytes = input.ToArray();
        if (bytes.Length == 0 || !HasValidSignature(bytes, contentType, signature))
            return Results.BadRequest(new { message = "The uploaded file content does not match its declared image type." });

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var duplicate = await db.TripReceipts.AsNoTracking()
            .AnyAsync(x => x.TripId == tripId && x.Sha256 == sha, ct);
        var id = Guid.NewGuid();
        var storedName = id.ToString("N") + ExtensionFor(contentType);
        var root = StorageRoot(config);
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, storedName);
        await File.WriteAllBytesAsync(destination, bytes, ct);

        var receipt = new TripReceipt
        {
            Id = id, TripId = tripId, ExpenseId = expenseId, UploadedByUserId = userId,
            OriginalFileName = SafeFileName(file.FileName), StoredFileName = storedName,
            ContentType = contentType, FileSizeBytes = bytes.Length, Sha256 = sha,
            ExtractionStatus = "pending_review",
            ExtractionWarning = duplicate ? "An identical receipt image already exists in this trip. Check for a duplicate before confirming." : null
        };

        try
        {
            var aiBaseUrl = config["AiService:BaseUrl"]?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(aiBaseUrl))
            {
                try
                {
                    var response = await clients.CreateClient("ai-receipts").PostAsJsonAsync(
                        $"{aiBaseUrl}/api/v1/receipts/extract",
                        new ReceiptExtractionPayload(contentType, Convert.ToBase64String(bytes)), ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var extraction = await response.Content.ReadFromJsonAsync<ReceiptExtractionResult>(cancellationToken: ct);
                        if (extraction is not null)
                        {
                            receipt.ExtractionStatus = extraction.Status == "extracted" ? "needs_review" : "pending_review";
                            receipt.MerchantName = Limit(extraction.MerchantName, 180);
                            receipt.ReceiptDate = extraction.ReceiptDate;
                            receipt.TotalAmount = extraction.TotalAmount is >= 0 and <= 999999999999999.9999m
                                ? extraction.TotalAmount : null;
                            receipt.CurrencyCode = IsCurrency(extraction.CurrencyCode) ? extraction.CurrencyCode : null;
                            receipt.Confidence = extraction.Confidence is >= 0 and <= 1 ? extraction.Confidence : null;
                            receipt.ExtractionWarning = JoinWarnings(receipt.ExtractionWarning, extraction.Warning);
                        }
                    }
                    else receipt.ExtractionWarning = JoinWarnings(receipt.ExtractionWarning, "Receipt saved; automatic extraction is unavailable. Review it manually.");
                }
                catch (HttpRequestException)
                {
                    receipt.ExtractionWarning = JoinWarnings(receipt.ExtractionWarning, "Receipt saved; extraction service is unavailable. Review it manually.");
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    receipt.ExtractionWarning = JoinWarnings(receipt.ExtractionWarning, "Receipt saved; extraction timed out. Review it manually.");
                }
            }
            else receipt.ExtractionWarning = JoinWarnings(receipt.ExtractionWarning, "Receipt saved without OCR. Configure AiService:BaseUrl to enable extraction.");

            db.TripReceipts.Add(receipt);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            try { File.Delete(destination); } catch (IOException) { }
            throw;
        }
        return Results.Created($"/api/v1/trips/{tripId}/receipts/{id}", ToResponse(receipt, duplicate));
    }

    private static bool HasValidSignature(byte[] bytes, string contentType, byte[] signature)
    {
        if (bytes.Length < signature.Length || !bytes.AsSpan(0, signature.Length).SequenceEqual(signature))
            return false;
        if (contentType != "image/webp") return true;
        return bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP";
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".webp"
    };

    private static string SafeFileName(string? name)
    {
        var clean = Path.GetFileName(name ?? "receipt").Replace("\\", "");
        return clean.Length > 255 ? clean[..255] : clean;
    }

    private static string? Limit(string? value, int length)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return null;
        return clean.Length > length ? clean[..length] : clean;
    }

    private static bool IsCurrency(string? value) =>
        value is { Length: 3 } && value.All(char.IsAsciiLetter) && value == value.ToUpperInvariant();

    private static string JoinWarnings(string? first, string second) =>
        string.Join(" ", new[] { first, second }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

    private static async Task<IResult> FileAsync(
        Guid tripId, Guid receiptId, ClaimsPrincipal principal,
        TravelOsDbContext db, IConfiguration config, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var receipt = await db.TripReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == receiptId && x.TripId == tripId, ct);
        if (receipt is null) return Results.NotFound();
        var path = Path.Combine(StorageRoot(config), receipt.StoredFileName);
        if (!File.Exists(path)) return Results.NotFound();
        return Results.File(path, receipt.ContentType, receipt.OriginalFileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> ConfirmAsync(
        Guid tripId, Guid receiptId, ConfirmReceiptRequest request,
        ClaimsPrincipal principal, TravelOsDbContext db, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var receipt = await db.TripReceipts.FirstOrDefaultAsync(
            x => x.Id == receiptId && x.TripId == tripId, ct);
        if (receipt is null) return Results.NotFound();
        if (receipt.UploadedByUserId != userId)
        {
            var owner = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
            if (!owner) return Results.Forbid();
        }
        var errors = new Dictionary<string, string[]>();
        var merchant = Limit(request.MerchantName, 180);
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant();
        if (request.MerchantName?.Length > 180) errors["merchantName"] = ["Merchant name must be 180 characters or fewer."];
        if (request.TotalAmount is < 0 or > 999999999999999.9999m) errors["totalAmount"] = ["Total amount is outside the supported range."];
        if (request.CurrencyCode is not null && !IsCurrency(currency)) errors["currencyCode"] = ["Currency must be a three-letter uppercase code."];
        if (request.ExpenseId.HasValue && !await db.TripExpenses.AnyAsync(
            x => x.Id == request.ExpenseId.Value && x.TripId == tripId, ct))
            errors["expenseId"] = ["The selected expense does not belong to this trip."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        receipt.MerchantName = merchant;
        receipt.ReceiptDate = request.ReceiptDate;
        receipt.TotalAmount = request.TotalAmount;
        receipt.CurrencyCode = currency;
        receipt.ExpenseId = request.ExpenseId;
        receipt.ExtractionStatus = "confirmed";
        receipt.ConfirmedByUserId = userId;
        receipt.ConfirmedAt = DateTimeOffset.UtcNow;
        receipt.ExtractionWarning = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(receipt, await db.TripReceipts.AnyAsync(
            x => x.Id != receipt.Id && x.TripId == tripId && x.Sha256 == receipt.Sha256, ct)));
    }

    private static async Task<IResult> DeleteAsync(
        Guid tripId, Guid receiptId, ClaimsPrincipal principal,
        TravelOsDbContext db, IConfiguration config, CancellationToken ct)
    {
        if (!TryGetUserId(principal, out var userId)) return Results.Unauthorized();
        if (!await IsMemberAsync(tripId, userId, db, ct)) return Results.NotFound();
        var receipt = await db.TripReceipts.FirstOrDefaultAsync(
            x => x.Id == receiptId && x.TripId == tripId, ct);
        if (receipt is null) return Results.NotFound();
        if (receipt.UploadedByUserId != userId)
        {
            var owner = await db.Trips.AnyAsync(x => x.Id == tripId && x.OwnerUserId == userId, ct);
            if (!owner) return Results.Forbid();
        }
        var path = Path.Combine(StorageRoot(config), receipt.StoredFileName);
        db.TripReceipts.Remove(receipt);
        await db.SaveChangesAsync(ct);
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        return Results.NoContent();
    }
}
