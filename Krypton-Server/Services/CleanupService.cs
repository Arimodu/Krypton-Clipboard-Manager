using Krypton.Server.Configuration;
using Krypton.Server.Database;
using Krypton.Server.Database.Repositories;
using Krypton.Shared.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Krypton.Server.Services;

/// <summary>
/// Background service that periodically cleans up old clipboard entries.
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServerConfiguration _config;
    private readonly ILogger<CleanupService> _logger;
    private readonly TimeSpan _interval;

    public CleanupService(
        IServiceScopeFactory scopeFactory,
        ServerConfiguration config,
        ILogger<CleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;

        // Use configured interval, default to 1 hour
        var intervalHours = Math.Max(1, config.Cleanup.IntervalHours);
        _interval = TimeSpan.FromHours(intervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Cleanup.Enabled)
        {
            _logger.LogInformation("Automatic cleanup is disabled");
            return;
        }

        _logger.LogInformation(
            "Cleanup service started. Will delete entries older than {Days} days",
            _config.Cleanup.Days);

        // Initial delay to let the server fully start
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Cleanup service stopped");
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IClipboardEntryRepository>();

        var deletedCount = await repository.CleanupOldEntriesAsync(_config.Cleanup.Days);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Cleanup completed: deleted {Count} entries older than {Days} days",
                deletedCount,
                _config.Cleanup.Days);
        }
        else
        {
            _logger.LogDebug("Cleanup check completed: no old entries to delete");
        }

        await RunImageCleanupAsync(scope, cancellationToken);
    }

    private async Task RunImageCleanupAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        if (_config.Images.RetentionDays <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-_config.Images.RetentionDays);
        var db = scope.ServiceProvider.GetRequiredService<KryptonDbContext>();

        var oldImages = await db.ClipboardEntries
            .Where(e => e.ContentType == ClipboardContentType.Image && e.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (oldImages.Count == 0)
        {
            _logger.LogDebug("Image cleanup: no old image entries to delete");
            return;
        }

        foreach (var img in oldImages)
        {
            if (img.ExternalStoragePath != null)
            {
                var fullPath = Path.Combine(_config.Images.StoragePath, img.ExternalStoragePath);
                try
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete image file: {Path}", fullPath);
                }
            }
        }

        db.ClipboardEntries.RemoveRange(oldImages);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Image cleanup completed: deleted {Count} image entries older than {Days} days",
            oldImages.Count,
            _config.Images.RetentionDays);
    }
}
