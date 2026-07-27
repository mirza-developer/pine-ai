using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PineAI.Bots.Bale.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Bale.Services;

namespace PineAI.Bots.Bale.Workers;

/// <summary>
/// Background worker that continuously long-polls the Bale Bot API for new updates
/// and dispatches them concurrently to <see cref="IBotUpdateHandler"/>.
///
/// Concurrency model:
///   - A global semaphore (<see cref="MaxConcurrentUpdates"/>) caps the total number
///     of updates processed at the same time, preventing thread-pool and DB-connection
///     pool exhaustion.
///   - A per-user semaphore (one per chat ID) ensures that messages from the same user
///     are always processed in arrival order, preserving AI session integrity.
///   - <c>stoppingToken</c> is forwarded as-is; no additional per-request timeout is
///     added, so group messages and photo forwards are never silently dropped.
/// </summary>
public class BaleBotWorker : BackgroundService
{
    /// <summary>Timeout in seconds for each long-poll request sent to getUpdates.</summary>
    private const int LongPollTimeoutSeconds = 30;

    /// <summary>Maximum number of updates processed concurrently across all users.</summary>
    private const int MaxConcurrentUpdates = 10;

    private readonly SemaphoreSlim globalSemaphore = new(MaxConcurrentUpdates, MaxConcurrentUpdates);
    private readonly ConcurrentDictionary<long, SemaphoreSlim> perUserSemaphores = new();

    private readonly IServiceScopeFactory scopeFactory;
    private readonly BaleBotClient botClient;
    private readonly ILogger<BaleBotWorker> logger;

    public BaleBotWorker(IServiceScopeFactory scopeFactory, BaleBotClient botClient, ILogger<BaleBotWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.botClient = botClient;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BaleBotWorker started");

        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await botClient.GetUpdatesAsync(offset, LongPollTimeoutSeconds, stoppingToken);

                // Advance offset synchronously before launching any task so no update
                // is ever double-processed even if a task throws.
                foreach (var update in updates)
                {
                    if (update.UpdateId >= offset)
                        offset = update.UpdateId + 1;
                }

                // Launch all updates in this batch concurrently and wait for the
                // whole batch to drain before polling again.
                var tasks = new List<Task>(updates.Count);
                foreach (var update in updates)
                    tasks.Add(ProcessUpdateAsync(update, stoppingToken));

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in BaleBotWorker poll loop; retrying in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("BaleBotWorker stopped");
    }

    /// <summary>
    /// Processes a single update with dual-semaphore concurrency control:
    /// the global semaphore limits total concurrency; the per-user semaphore
    /// serialises messages from the same chat so sessions stay coherent.
    /// </summary>
    private async Task ProcessUpdateAsync(BaleUpdate update, CancellationToken stoppingToken)
    {
        var chatId = update.Message?.Chat?.Id ?? 0;
        var userSemaphore = perUserSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));

        await globalSemaphore.WaitAsync(stoppingToken);
        try
        {
            await userSemaphore.WaitAsync(stoppingToken);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IBotUpdateHandler>();
                await handler.HandleAsync(update, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling update {UpdateId}", update.UpdateId);
            }
            finally
            {
                userSemaphore.Release();
            }
        }
        finally
        {
            globalSemaphore.Release();
        }
    }
}
