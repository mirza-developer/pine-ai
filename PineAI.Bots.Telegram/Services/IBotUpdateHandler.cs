using Telegram.Bot.Types;

namespace PineAI.Bots.Telegram.Services;

/// <summary>Processes a single incoming Telegram update.</summary>
public interface IBotUpdateHandler
{
    Task HandleAsync(Update update, CancellationToken ct);
}
