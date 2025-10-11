using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.DevUI.Models.Execution;

/// <summary>
/// DevUI execution request model compatible with OpenAI Responses API format
/// Matches Python's AgentFrameworkRequest model
/// </summary>
public class DevUIExecutionRequest
{
    /// <summary>
    /// Model field - used as entity_id in DevUI (agent/workflow name)
    /// This matches OpenAI's standard where 'model' identifies the entity to execute
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "agent-framework";

    /// <summary>
    /// Input for the entity (string OR array of content items)
    /// Matches Python: input: str | list[Any]
    /// </summary>
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    /// <summary>
    /// Enable streaming responses via Server-Sent Events
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>
    /// Conversation ID for context (OpenAI standard)
    /// Supports both string ("conv_123") and object ({"id": "conv_123"}) formats
    /// </summary>
    [JsonPropertyName("conversation")]
    public object? Conversation { get; set; }

    /// <summary>
    /// Optional instructions override
    /// </summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    /// <summary>
    /// Optional metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Temperature for model sampling
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Maximum output tokens
    /// </summary>
    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Tools available to the model
    /// </summary>
    [JsonPropertyName("tools")]
    public List<Dictionary<string, object>>? Tools { get; set; }

    /// <summary>
    /// Optional extra body for advanced use cases
    /// </summary>
    [JsonPropertyName("extra_body")]
    public Dictionary<string, object>? ExtraBody { get; set; }

    /// <summary>
    /// Get entity ID from model field (NOT from extra_body)
    /// In DevUI, model IS the entity_id. Simple and clean!
    /// Note: This is a method instead of property because it parallels GetConversationId()
    /// and GetLastMessageContent() for consistency in the API.
    /// </summary>
#pragma warning disable CA1024 // Use properties where appropriate
    public string GetEntityId()
#pragma warning restore CA1024 // Use properties where appropriate
    {
        return Model;
    }

    /// <summary>
    /// Extract conversation_id from conversation parameter
    /// Supports both string and object forms
    /// </summary>
    public string? GetConversationId()
    {
        if (Conversation is string conversationStr)
        {
            return conversationStr;
        }
        if (Conversation is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("id", out var idProp))
        {
            return idProp.GetString();
        }
        if (Conversation is Dictionary<string, object> dict && dict.TryGetValue("id", out var id))
        {
            return id?.ToString();
        }
        return null;
    }

    /// <summary>
    /// Get input as string (for simple text input or serialized complex input)
    /// </summary>
    public string GetInputAsString()
    {
        if (Input == null)
            return string.Empty;

        if (Input.Value.ValueKind == JsonValueKind.String)
        {
            return Input.Value.GetString() ?? string.Empty;
        }

        // For array/object, return JSON representation
        return Input.Value.GetRawText();
    }

    /// <summary>
    /// Check if input is an array
    /// </summary>
    public bool IsInputArray() => Input?.ValueKind == JsonValueKind.Array;

    /// <summary>
    /// Get last message content - handles both string and array formats
    /// </summary>
    public string GetLastMessageContent()
    {
        if (Input == null)
            return string.Empty;

        // If string input, return directly
        if (Input.Value.ValueKind == JsonValueKind.String)
        {
            return Input.Value.GetString() ?? string.Empty;
        }

        // If array input, extract text from first message content
        if (Input.Value.ValueKind == JsonValueKind.Array)
        {
            var items = Input.Value.EnumerateArray();
            foreach (var item in items)
            {
                if (item.TryGetProperty("type", out var typeVal) &&
                    typeVal.GetString() == "message" &&
                    item.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contentItem in content.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out var contentType) &&
                            contentType.GetString() == "input_text" &&
                            contentItem.TryGetProperty("text", out var text))
                        {
                            return text.GetString() ?? string.Empty;
                        }
                    }
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Convert to ChatMessage array for Agent Framework
    /// </summary>
    public ChatMessage[] ToChatMessages()
    {
        var inputStr = GetLastMessageContent();
        if (!string.IsNullOrEmpty(inputStr))
        {
            return new[] { new ChatMessage(ChatRole.User, inputStr) };
        }
        return Array.Empty<ChatMessage>();
    }
}
