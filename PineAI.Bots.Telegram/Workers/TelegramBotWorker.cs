using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PineAI.Bots.Telegram.Workers;

/// <summary>
/// Background worker that continuously long-polls the Telegram Bot API for new updates
/// and dispatches them concurrently to <see cref="IBotUpdateHandler"/>.
///
/// Concurrency model:
///   - A global semaphore (<see cref="MaxConcurrentUpdates"/>) caps the total number
///     of updates processed at the same time.
///   - A per-user semaphore ensures messages from the same user are always processed
///     in arrival order, preserving AI session integrity.
/// </summary>
public class TelegramBotWorker : BackgroundService
{
    /// <summary>Timeout in seconds for each long-poll request sent to getUpdates.</summary>
    private const int LongPollTimeoutSeconds = 30;

    /// <summary>Maximum number of updates processed concurrently across all users.</summary>
    private const int MaxConcurrentUpdates = 10;

    private readonly SemaphoreSlim globalSemaphore = new(MaxConcurrentUpdates, MaxConcurrentUpdates);
    private readonly ConcurrentDictionary<long, SemaphoreSlim> perUserSemaphores = new();

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ITelegramBotClient botClient;
    private readonly ILogger<TelegramBotWorker> logger;

    public TelegramBotWorker(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient botClient,
        ILogger<TelegramBotWorker> logger)
    {
        this.scopeFactory = scopeFactory;
        this.botClient = botClient;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelegramBotWorker started");

        int offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await botClient.GetUpdates(
                    offset: offset,
                    limit: 100,
                    timeout: LongPollTimeoutSeconds,
                    allowedUpdates: [UpdateType.Message],
                    cancellationToken: stoppingToken);

                // Advance offset synchronously before launching any task so no update
                // is ever double-processed even if a task throws.
                foreach (var update in updates)
                {
                    if (update.Id >= offset)
                        offset = update.Id + 1;
                }

                var tasks = new List<Task>(updates.Length);
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
                logger.LogError(ex, "Unexpected error in TelegramBotWorker poll loop; retrying in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("TelegramBotWorker stopped");
    }

    /// <summary>
    /// Processes a single update with dual-semaphore concurrency control.
    /// </summary>
    private async Task ProcessUpdateAsync(Update update, CancellationToken stoppingToken)
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
                logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
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
