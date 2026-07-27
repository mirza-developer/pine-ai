using System.Collections.Concurrent;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Thread-safe in-memory store that maps bot chat IDs to serialized
/// <c>AgentSession</c> JSON strings. Each user has exactly one
/// session which is replaced when the user sends <c>/start</c>.
/// </summary>
public class ChatSessionStore
{
    private readonly ConcurrentDictionary<long, string> sessions = new();

    /// <summary>Returns the serialized session JSON for the given chat, or <c>null</c> if none exists.</summary>
    public string? GetSession(long chatId)
        => sessions.TryGetValue(chatId, out var json) ? json : null;

    /// <summary>Stores (or replaces) the serialized session JSON for the given chat.</summary>
    public void SetSession(long chatId, string sessionJson)
        => sessions[chatId] = sessionJson;

    /// <summary>Removes the session for the given chat (e.g. on <c>/start</c>).</summary>
    public void RemoveSession(long chatId)
        => sessions.TryRemove(chatId, out _);
}
