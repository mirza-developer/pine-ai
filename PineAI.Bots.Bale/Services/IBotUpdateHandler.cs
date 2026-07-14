using PineAI.Bots.Bale.Models;

namespace PineAI.Bots.Bale.Services;

/// <summary>Processes a single incoming Bale update.</summary>
public interface IBotUpdateHandler
{
    Task HandleAsync(BaleUpdate update, CancellationToken ct);
}
