using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Receipts;

/// <summary>
/// Retries durable receipt deletion requests. Deleting the file before removing its
/// metadata makes retries idempotent: if the process stops after File.Delete, the next
/// pass observes the missing file and completes the metadata removal.
/// </summary>
public sealed class ReceiptFileDeletionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ReceiptFileDeletionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Receipt deletion batch failed ({FailureType}); pending requests will be retried.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        var root = Path.GetFullPath(configuration["Receipts:StoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "private-receipts"));
        Directory.CreateDirectory(root);
        var separator = Path.DirectorySeparatorChar;
        var rootPrefix = root.EndsWith(separator) ? root : root + separator;

        // A transaction-scoped advisory lock prevents multiple API replicas from
        // processing the same receipt queue concurrently. The lock releases on commit,
        // rollback or process/connection loss.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var acquired = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(73190551234055) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!acquired)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var pending = await db.TripReceipts
            .Where(receipt => receipt.DeletionRequestedAt != null &&
                (receipt.NextDeletionAttemptAt == null || receipt.NextDeletionAttemptAt <= now))
            .OrderBy(receipt => receipt.DeletionRequestedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var receipt in pending)
        {
            try
            {
                var fileName = receipt.StoredFileName;
                if (string.IsNullOrWhiteSpace(fileName) ||
                    !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
                    throw new InvalidOperationException("Stored receipt filename is not a safe leaf filename.");

                var path = Path.GetFullPath(Path.Combine(root, fileName));
                if (!path.StartsWith(rootPrefix, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("Stored receipt path escapes the configured storage root.");

                // File.Delete is idempotent when the file is already absent.
                File.Delete(path);
                receipt.FileDeletedAt = DateTimeOffset.UtcNow;
                db.TripReceipts.Remove(receipt);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Completed receipt file deletion for receipt {ReceiptId}.", receipt.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                receipt.DeletionAttempts = checked(receipt.DeletionAttempts + 1);
                receipt.LastDeletionError = exception.GetType().Name;
                var backoffSeconds = Math.Min(3600, 15 * Math.Pow(2, Math.Min(receipt.DeletionAttempts - 1, 8)));
                receipt.NextDeletionAttemptAt = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception persistenceException)
                {
                    logger.LogError("Could not persist retry state for receipt {ReceiptId} ({FailureType}).",
                        receipt.Id, persistenceException.GetType().Name);
                }

                logger.LogWarning(
                    "Receipt file deletion failed for receipt {ReceiptId}; attempt {Attempt}, retry after {NextAttemptAt}, failure {FailureType}.",
                    receipt.Id, receipt.DeletionAttempts, receipt.NextDeletionAttemptAt, exception.GetType().Name);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
