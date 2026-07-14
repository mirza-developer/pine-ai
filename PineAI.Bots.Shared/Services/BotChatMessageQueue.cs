using System.Threading.Channels;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Thread-safe, bounded channel used to pass bot conversation messages to
/// <see cref="Workers.BotChatMessageSaverWorker"/> for background persistence.
/// The bot enqueues via <see cref="TryEnqueue"/> (never blocks); the worker
/// dequeues and writes to the database.
/// </summary>
public sealed class BotChatMessageQueue
{
    /// <summary>
    /// Maximum number of messages that can be buffered before the oldest entry is
    /// silently dropped. 10 000 covers several minutes of very high traffic without
    /// consuming significant memory (~1 KB per entry ≈ 10 MB worst-case).
    /// </summary>
    private const int ChannelCapacity = 10_000;

    private readonly Channel<BotChatMessageEntry> channel =
        Channel.CreateBounded<BotChatMessageEntry>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

    public ChannelReader<BotChatMessageEntry> Reader => channel.Reader;

    /// <summary>
    /// Enqueues a message entry without blocking.
    /// If the channel is full the oldest entry is silently dropped.
    /// </summary>
    public void TryEnqueue(BotChatMessageEntry entry) =>
        channel.Writer.TryWrite(entry);
}

/// <summary>A single bot conversation message to be persisted.</summary>
public sealed record BotChatMessageEntry(
    string BaleUsername,
    long ChatId,
    string MessageText,
    bool IsFromBot,
    DateTime SentAt);
