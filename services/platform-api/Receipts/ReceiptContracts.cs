namespace TravelOS.PlatformApi.Receipts;

public sealed record ReceiptExtractionPayload(
    string ContentType, string ImageBase64);

public sealed record ReceiptExtractionResult(
    string Status, string Provider, string? MerchantName,
    DateOnly? ReceiptDate, decimal? TotalAmount, string? CurrencyCode,
    decimal? Confidence, string Warning);

public sealed record ConfirmReceiptRequest(
    string? MerchantName, DateOnly? ReceiptDate, decimal? TotalAmount,
    string? CurrencyCode, Guid? ExpenseId);

public sealed record TripReceiptResponse(
    Guid Id, Guid TripId, Guid? ExpenseId, Guid UploadedByUserId,
    string OriginalFileName, string ContentType, long FileSizeBytes,
    string Sha256, string ExtractionStatus, string? MerchantName,
    DateOnly? ReceiptDate, decimal? TotalAmount, string? CurrencyCode,
    decimal? Confidence, string? ExtractionWarning, DateTimeOffset UploadedAt,
    DateTimeOffset? ConfirmedAt, Guid? ConfirmedByUserId, bool IsDuplicate);
