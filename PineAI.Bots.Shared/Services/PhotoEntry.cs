namespace PineAI.Bots.Shared.Services;

internal sealed class PhotoEntry(long messageId)
{
    public List<long> MessageIds { get; } = [messageId];
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}
