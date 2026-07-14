using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Bale.Models;

namespace PineAI.Bots.Bale.Services;

/// <summary>
/// Thin wrapper over the Bale Bot HTTP API.
/// Base URL: https://tapi.bale.ai/bot{token}/
/// </summary>
public class BaleBotClient
{
    private readonly HttpClient httpClient;
    private readonly ILogger<BaleBotClient> logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BaleBotClient(IHttpClientFactory httpClientFactory, ILogger<BaleBotClient> logger)
    {
        this.httpClient = httpClientFactory.CreateClient("BaleBotClient");
        this.logger = logger;
    }

    /// <summary>
    /// Calls getUpdates with long-polling to receive pending updates.
    /// </summary>
    /// <param name="offset">Identifier of the first update to be returned (last_update_id + 1).</param>
    /// <param name="timeout">Timeout in seconds for long-polling. 0 = short-polling.</param>
    public async Task<List<BaleUpdate>> GetUpdatesAsync(long offset, int timeout, CancellationToken ct)
    {
        var url = $"getUpdates?offset={offset}&timeout={timeout}";
        try
        {
            var response = await httpClient.GetFromJsonAsync<BaleApiResponse<List<BaleUpdate>>>(url, JsonOptions, ct);
            if (response?.Ok == true && response.Result != null)
                return response.Result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling getUpdates (offset={Offset})", offset);
        }
        return new List<BaleUpdate>();
    }

    /// <summary>Sends a text message to the given chat.</summary>
    public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        var body = new BaleSendMessageRequest { ChatId = chatId, Text = text };
        try
        {
            var response = await httpClient.PostAsJsonAsync("sendMessage", body, ct);
            if (response.IsSuccessStatusCode)
                return true;

            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("sendMessage failed: {StatusCode} {Error}", response.StatusCode, error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception sending message to chat {ChatId}", chatId);
        }
        return false;
    }

    /// <summary>Forwards an existing message to the target chat.</summary>
    public async Task<bool> ForwardMessageAsync(long chatId, long fromChatId, long messageId, CancellationToken ct)
    {
        var body = new BaleForwardMessageRequest
        {
            ChatId = chatId,
            FromChatId = fromChatId,
            MessageId = messageId
        };
        try
        {
            var response = await httpClient.PostAsJsonAsync("forwardMessage", body, ct);
            if (response.IsSuccessStatusCode)
                return true;

            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("forwardMessage failed: {StatusCode} {Error}", response.StatusCode, error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception forwarding message {MessageId} from chat {FromChatId} to chat {ChatId}", messageId, fromChatId, chatId);
        }
        return false;
    }
}
