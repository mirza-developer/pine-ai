namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Represents a response from the chat agent service including the response text and updated session.
/// </summary>
public class ChatAgentResponse
{
    public string ResponseText { get; set; } = string.Empty;
    public string SerializedSession { get; set; } = string.Empty;
}
