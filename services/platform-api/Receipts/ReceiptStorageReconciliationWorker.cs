using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

namespace TravelOS.PlatformApi.Receipts;

/// <summary>
/// Periodically detects receipt files without metadata and metadata rows whose files
/// are missing. Cleanup is dry-run unless explicitly enabled by an operator.
/// </summary>
public sealed class ReceiptStorageReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ReceiptStorageReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(configuration["Receipts:StoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "private-receipts"));
        if (!Directory.Exists(root))
        {
            logger.LogWarning("Receipt reconciliation skipped because the configured storage directory is unavailable.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelOsDbContext>();
        var maximumFilesPerRun = Math.Clamp(
            configuration.GetValue<int?>("Receipts:ReconciliationMaximumFilesPerRun") ?? 10_000,
            100, 100_000);
        var knownNames = await db.TripReceipts.AsNoTracking()
            .OrderBy(receipt => receipt.Id)
            .Select(receipt => receipt.StoredFileName)
            .Take(maximumFilesPerRun + 1)
            .ToListAsync(cancellationToken);
        var metadataScanTruncated = knownNames.Count > maximumFilesPerRun;
        var known = new HashSet<string>(
            knownNames.Take(maximumFilesPerRun), StringComparer.Ordinal);
        var minimumAgeHours = Math.Clamp(
            configuration.GetValue<int?>("Receipts:OrphanMinimumAgeHours") ?? 24, 1, 24 * 365);
        var cutoff = DateTime.UtcNow.AddHours(-minimumAgeHours);
        var cleanupEnabled = configuration.GetValue<bool>("Receipts:OrphanCleanupEnabled");

        var orphanCount = 0;
        var deletedCount = 0;
        var missingMetadataFileCount = 0;
        var failureCount = 0;

        foreach (var name in known)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
                missingMetadataFileCount++;
        }

        var scannedFileCount = 0;
        var filesystemScanTruncated = false;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scannedFileCount >= maximumFilesPerRun)
            {
                filesystemScanTruncated = true;
                break;
            }
            scannedFileCount++;
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    (attributes & FileAttributes.Directory) != 0)
                    continue;

                var name = Path.GetFileName(path);
                if (known.Contains(name) || File.GetLastWriteTimeUtc(path) > cutoff)
                    continue;

                orphanCount++;
                if (cleanupEnabled)
                {
                    File.Delete(path);
                    deletedCount++;
                }
            }
            catch (IOException)
            {
                failureCount++;
            }
            catch (UnauthorizedAccessException)
            {
                failureCount++;
            }
        }

        logger.LogInformation(
            "Receipt storage reconciliation completed. Orphans={OrphanCount}, Deleted={DeletedCount}, MissingFiles={MissingFileCount}, Failures={FailureCount}, CleanupEnabled={CleanupEnabled}, MinimumAgeHours={MinimumAgeHours}, ScannedFiles={ScannedFiles}, MaximumFilesPerRun={MaximumFilesPerRun}, MetadataScanTruncated={MetadataScanTruncated}, FilesystemScanTruncated={FilesystemScanTruncated}.",
            orphanCount, deletedCount, missingMetadataFileCount, failureCount, cleanupEnabled,
            minimumAgeHours, scannedFileCount, maximumFilesPerRun, metadataScanTruncated, filesystemScanTruncated);
    }
}
