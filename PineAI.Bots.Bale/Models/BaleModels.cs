using System.Text.Json.Serialization;

namespace PineAI.Bots.Bale.Models;

/// <summary>Wrapper returned by every Bale Bot API call.</summary>
public class BaleApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }
}

/// <summary>A single update received from the Bale Bot API via getUpdates.</summary>
public class BaleUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public BaleMessage? Message { get; set; }
}

/// <summary>A message sent by a user inside a Bale chat.</summary>
public class BaleMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("from")]
    public BaleUser? From { get; set; }

    [JsonPropertyName("chat")]
    public BaleChat Chat { get; set; } = null!;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("photo")]
    public PhotoSize[]? Photo { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }
}

/// <summary>Represents one size variant of a photo or a file/sticker thumbnail.</summary>
public class PhotoSize
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("file_unique_id")]
    public string FileUniqueId { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; set; }
}

/// <summary>Information about a Bale user who sent a message.</summary>
public class BaleUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

/// <summary>The chat (conversation) where the message was sent.</summary>
public class BaleChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>Request body for the sendMessage API call.</summary>
public class BaleSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public long ChatId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>Request body for the forwardMessage API call.</summary>
public class BaleForwardMessageRequest
{
    [JsonPropertyName("chat_id")]
    public long ChatId { get; set; }

    [JsonPropertyName("from_chat_id")]
    public long FromChatId { get; set; }

    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }
}
