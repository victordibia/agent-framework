using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.DevUI.Models;

/// <summary>
/// Conversation object matching OpenAI Conversations API
/// </summary>
public class Conversation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "conversation";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Conversation item (message) matching OpenAI format
/// </summary>
public class ConversationItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "conversation.item";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public List<ConversationContent> Content { get; set; } = new();

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Content within a conversation item
/// </summary>
public class ConversationContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Conversation list response
/// </summary>
public class ConversationListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("data")]
    public List<Conversation> Data { get; set; } = new();

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    [JsonPropertyName("first_id")]
    public string? FirstId { get; set; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; set; }
}

/// <summary>
/// Conversation item list response
/// </summary>
public class ConversationItemListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("data")]
    public List<ConversationItem> Data { get; set; } = new();

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    [JsonPropertyName("first_id")]
    public string? FirstId { get; set; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; set; }
}

/// <summary>
/// Conversation deletion response
/// </summary>
public class ConversationDeletedResource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "conversation.deleted";

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; } = true;
}

/// <summary>
/// Request to create a conversation
/// </summary>
public class CreateConversationRequest
{
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Request to update a conversation
/// </summary>
public class UpdateConversationRequest
{
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Request to add items to a conversation
/// </summary>
public class AddConversationItemsRequest
{
    [JsonPropertyName("items")]
    public List<Dictionary<string, object>> Items { get; set; } = new();
}
