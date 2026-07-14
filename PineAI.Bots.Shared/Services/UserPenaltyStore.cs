using System.Collections.Concurrent;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Thread-safe in-memory store that tracks active penalty locks per chat ID.
/// A penalty prevents the user from reaching the AI for <see cref="PenaltyDuration"/>.
/// Expired entries are evicted lazily on every <see cref="IsUnderPenalty"/> call and
/// periodically by <see cref="Workers.PenaltyStoreCleanupWorker"/>.
/// </summary>
public class UserPenaltyStore
{
    public static readonly TimeSpan PenaltyDuration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<long, PenaltyEntry> penalties = new();

    /// <summary>Applies a fresh 10-minute penalty lock for the given chat.</summary>
    public void ApplyPenalty(long chatId)
        => penalties[chatId] = new PenaltyEntry(DateTime.UtcNow + PenaltyDuration);

    /// <summary>
    /// Returns <c>true</c> when the user is currently under a penalty lock.
    /// Self-evicts the entry if it has already expired.
    /// </summary>
    public bool IsUnderPenalty(long chatId)
    {
        if (!penalties.TryGetValue(chatId, out var entry))
            return false;

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            penalties.TryRemove(chatId, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes all entries whose penalty has expired.
    /// Called periodically by <see cref="Workers.PenaltyStoreCleanupWorker"/>.
    /// </summary>
    public int EvictExpired()
    {
        var evicted = 0;
        foreach (var key in penalties.Keys)
        {
            if (penalties.TryGetValue(key, out var entry) && entry.ExpiresAt <= DateTime.UtcNow)
            {
                if (penalties.TryRemove(key, out _))
                    evicted++;
            }
        }
        return evicted;
    }

    private sealed record PenaltyEntry(DateTime ExpiresAt);
}
