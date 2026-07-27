using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Shared.Services;
using PineAI.Core.Entities;
using PineAI.Persistence.Services;

namespace PineAI.Bots.Shared.Workers;

/// <summary>
/// Background worker that drains <see cref="BotChatMessageQueue"/> and persists
/// each entry to the database using its own DI scope.
/// Running separately from the main bot loop ensures that database I/O never
/// adds latency to message handling.
/// </summary>
public sealed class BotChatMessageSaverWorker : BackgroundService
{
    private readonly BotChatMessageQueue queue;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<BotChatMessageSaverWorker> logger;

    public BotChatMessageSaverWorker(
        BotChatMessageQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BotChatMessageSaverWorker> logger)
    {
        this.queue = queue;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BotChatMessageSaverWorker started");

        await foreach (var entry in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PineAIDbContext>();

                db.BotChatMessage.Add(new BotChatMessage
                {
                    BaleUsername = entry.BaleUsername,
                    ChatId = entry.ChatId,
                    MessageText = entry.MessageText,
                    IsFromBot = entry.IsFromBot,
                    SentAt = entry.SentAt
                });

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist bot chat message for user {Username}", entry.BaleUsername);
            }
        }

        logger.LogInformation("BotChatMessageSaverWorker stopped");
    }
}
