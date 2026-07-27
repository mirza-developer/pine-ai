using System.Collections.Concurrent;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Thread-safe in-memory store that remembers all photo messages sent by each
/// user within a conversation turn. Used to forward photos to support staff
/// when the AI generates a FEEDBACK block that includes <c>HasPhoto: true</c>.
/// Entries not consumed within <see cref="EntryTtl"/> are evicted by
/// <see cref="EvictExpired"/>, which is called periodically by a background worker.
/// </summary>
public class PhotoMessageStore
{
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<long, PhotoEntry> photos = new();

    /// <summary>
    /// Appends a photo message ID for the given user chat.
    /// Multiple photos sent in the same turn are all retained until consumed or expired.
    /// </summary>
    public void StorePhoto(long userChatId, long messageId)
    {
        photos.AddOrUpdate(
            userChatId,
            _ => new PhotoEntry(messageId),
            (_, existing) =>
            {
                // AddOrUpdate holds an internal bucket lock, so no extra lock is needed.
                existing.MessageIds.Add(messageId);
                return existing;
            });
    }

    /// <summary>
    /// Removes and returns all stored photo message IDs for the given user chat,
    /// or an empty list if none exist.
    /// </summary>
    public IReadOnlyList<long> TakePhotos(long userChatId)
    {
        if (photos.TryRemove(userChatId, out var entry))
            return entry.MessageIds;
        return [];
    }

    /// <summary>
    /// Returns all stored photo message IDs for the given user chat without
    /// removing them, or an empty list if none exist.
    /// </summary>
    public IReadOnlyList<long> PeekPhotos(long userChatId)
    {
        if (photos.TryGetValue(userChatId, out var entry))
            return entry.MessageIds;
        return [];
    }

    /// <summary>
    /// Removes all entries older than <see cref="EntryTtl"/>.
    /// Called periodically by <c>PhotoMessageStoreCleanupWorker</c>.
    /// </summary>
    public int EvictExpired()
    {
        var cutoff = DateTime.UtcNow - EntryTtl;
        var evicted = 0;
        foreach (var key in photos.Keys)
        {
            if (photos.TryRemove(key, out var entry))
            {
                if (entry.CreatedAt < cutoff)
                {
                    evicted++;
                }
                else
                {
                    // Entry was refreshed in the meantime — put it back.
                    photos.TryAdd(key, entry);
                }
            }
        }
        return evicted;
    }
}
