using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

namespace PineAI.Bots.Shared.Services;

/// <summary>
/// Wraps the Microsoft Agents AI SDK to provide a session-aware AI chat agent.
/// </summary>
public class ChatAgentService : IChatAgentService
{
    private readonly IConfiguration configuration;
    private readonly ILogger<ChatAgentService> logger;

    private IChatClient? chatClient;
    private AIAgent? agent;

    public ChatAgentService(IConfiguration configuration, ILogger<ChatAgentService> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    /// <summary>
    /// Initializes the underlying <see cref="AIAgent"/>. Must be called once before using
    /// <see cref="SendWithSessionAsync"/>.
    /// </summary>
    public async Task InitAsync()
    {
        var apiKey = configuration["AiAgent:ApiKey"];
        var model = configuration["AiAgent:Model"] ?? "gpt-4.1";
        var endpoint = configuration["AiAgent:Endpoint"] ?? "https://models.github.ai/inference";

        chatClient = new ChatClient(
            model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .AsIChatClient();

        var instructions = await LoadInstructionsAsync();

        agent = new ChatClientAgent(chatClient,
            new ChatClientAgentOptions
            {
                ChatOptions = new()
                {
                    Instructions = instructions,
                }
            });

        logger.LogInformation("ChatAgentService initialized (model={Model}, endpoint={Endpoint})", model, endpoint);
    }

    /// <summary>
    /// Sends a user message to the agent within a persistent session.
    /// </summary>
    public async Task<ChatAgentResponse> SendWithSessionAsync(
        string? sessionJson, string userText)
    {
        EnsureInitialized();

        AgentSession session;
        if (!string.IsNullOrWhiteSpace(sessionJson))
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(sessionJson);
            session = await agent!.DeserializeSessionAsync(jsonElement);
        }
        else
        {
            session = await agent!.CreateSessionAsync();
        }

        var result = await agent!.RunAsync(userText, session);
        var responseText = result?.ToString() ?? string.Empty;

        var serializedElement = await agent!.SerializeSessionAsync(session);
        var serializedSession = serializedElement.GetRawText();

        return new ChatAgentResponse { ResponseText = responseText, SerializedSession = serializedSession };
    }

    /// <summary>Creates a fresh blank session and returns its serialized JSON.</summary>
    public async Task<string> CreateNewSessionJsonAsync()
    {
        EnsureInitialized();
        var session = await agent!.CreateSessionAsync();
        var element = await agent!.SerializeSessionAsync(session);
        return element.GetRawText();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (agent == null)
            throw new InvalidOperationException("ChatAgentService has not been initialized. Call InitAsync() first.");
    }

    /// <summary>
    /// Loads all instruction Markdown files from the <c>Chat/</c> folder next to the
    /// running assembly and concatenates them into a single system-prompt string.
    /// </summary>
    public static async Task<string> LoadInstructionsAsync()
    {
        var chatDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chat");
        if (!Directory.Exists(chatDir))
            return string.Empty;

        var lines = new List<string>();
        foreach (var filePath in Directory.GetFiles(chatDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var content = await File.ReadAllTextAsync(filePath);
            lines.Add($"=== {Path.GetFileName(filePath)} ===");
            lines.Add(content);
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
