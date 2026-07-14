namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Contract for a session-aware AI chat agent used by bot implementations.
/// </summary>
public interface IChatAgentService
{
    /// <summary>
    /// Initializes the underlying AI client. Must be called once before using
    /// <see cref="SendWithSessionAsync"/>.
    /// </summary>
    Task InitAsync();

    /// <summary>
    /// Sends a user message to the agent within a persistent session.
    /// </summary>
    /// <param name="sessionJson">Serialized session JSON from a previous turn, or <c>null</c> to start a new session.</param>
    /// <param name="userText">The user's message text.</param>
    /// <returns>The agent's response including the response text and the updated serialized session JSON.</returns>
    Task<ChatAgentResponse> SendWithSessionAsync(
        string? sessionJson, string userText);

    /// <summary>Creates a fresh blank session and returns its serialized JSON.</summary>
    Task<string> CreateNewSessionJsonAsync();
}
