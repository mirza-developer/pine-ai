using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Shared.Services;

namespace PineAI.Bots.Shared.Workers;

/// <summary>
/// Periodically evicts photo entries from <see cref="PhotoMessageStore"/> that
/// have not been consumed within <see cref="PhotoMessageStore.EntryTtl"/> (5 minutes).
/// Runs on the same interval as the TTL so stale entries are removed promptly.
/// </summary>
public class PhotoMessageStoreCleanupWorker(
    PhotoMessageStore photoMessageStore,
    ILogger<PhotoMessageStoreCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PhotoMessageStore.EntryTtl, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var evicted = photoMessageStore.EvictExpired();
            if (evicted > 0)
                logger.LogInformation("PhotoMessageStore: evicted {Count} expired photo entr{Suffix}",
                    evicted, evicted == 1 ? "y" : "ies");
        }
    }
}
