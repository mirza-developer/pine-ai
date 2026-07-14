using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// A session-aware AI chat agent that uses the ArvanCloud AIaaS API
/// (OpenAI-compatible REST endpoint) as its backend.
/// Session state is maintained as a plain JSON array of chat messages so it can
/// be persisted and restored across requests without any third-party SDK.
/// </summary>
public class ArvanChatAgentService : IChatAgentService
{
    private readonly IConfiguration configuration;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<ArvanChatAgentService> logger;

    private HttpClient? httpClient;
    private string model = "openai/gpt-4o-mini";
    private string systemInstructions = string.Empty;

    public ArvanChatAgentService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<ArvanChatAgentService> logger)
    {
        this.configuration = configuration;
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task InitAsync()
    {
        var apiKey = configuration["ArvanAiAgent:ApiKey"] ?? string.Empty;
        model = configuration["ArvanAiAgent:Model"] ?? "openai/gpt-4o-mini";
        var endpoint = configuration["ArvanAiAgent:Endpoint"] ?? "https://text.arvancloud.ir/oai/v1";

        // Normalize the base address so relative paths resolve correctly.
        var baseAddress = endpoint.TrimEnd('/') + "/";

        httpClient = httpClientFactory.CreateClient("ArvanAiClient");
        httpClient.BaseAddress = new Uri(baseAddress);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("apikey", apiKey);

        systemInstructions = await ChatAgentService.LoadInstructionsAsync();

        logger.LogInformation(
            "ArvanChatAgentService initialized (model={Model}, endpoint={Endpoint})",
            model, endpoint);
    }

    /// <inheritdoc/>
    public async Task<ChatAgentResponse> SendWithSessionAsync(
        string? sessionJson, string userText)
    {
        EnsureInitialized();

        // ── Reconstruct conversation history ─────────────────────────────────
        List<ArvanMessage> history;
        if (!string.IsNullOrWhiteSpace(sessionJson))
        {
            history = JsonSerializer.Deserialize<List<ArvanMessage>>(sessionJson,
                          ArvanJsonOptions.Default)
                      ?? new List<ArvanMessage>();
        }
        else
        {
            history = new List<ArvanMessage>();
        }

        // Build the messages array sent to the API (system prompt + history + new turn)
        var messages = new List<ArvanMessage>();
        if (!string.IsNullOrWhiteSpace(systemInstructions))
            messages.Add(new ArvanMessage("system", systemInstructions));
        messages.AddRange(history);
        messages.Add(new ArvanMessage("user", userText));

        // ── Call ArvanCloud Chat Completions API ──────────────────────────────
        var requestBody = new ArvanChatRequest(model, messages);
        var response = await httpClient!.PostAsJsonAsync("chat/completions", requestBody, ArvanJsonOptions.Default);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        var responseText = responseBody
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        // ── Persist the updated history (without the system prompt) ───────────
        history.Add(new ArvanMessage("user", userText));
        history.Add(new ArvanMessage("assistant", responseText));

        var serializedSession = JsonSerializer.Serialize(history, ArvanJsonOptions.Default);

        return new ChatAgentResponse { ResponseText = responseText, SerializedSession = serializedSession };
    }

    /// <inheritdoc/>
    public Task<string> CreateNewSessionJsonAsync()
    {
        EnsureInitialized();
        // An empty history array is a valid "new session".
        return Task.FromResult("[]");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (httpClient == null)
            throw new InvalidOperationException(
                "ArvanChatAgentService has not been initialized. Call InitAsync() first.");
    }
}

internal sealed record ArvanMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record ArvanChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ArvanMessage> Messages);

internal static class ArvanJsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
