using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.DevUI.Models;

public class DevUIExecutionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "agent-framework";

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("input")]
    public string? Input { get; set; }

    [JsonPropertyName("messages")]
    public List<DevUIRequestMessage>? Messages { get; set; }

    [JsonPropertyName("extra_body")]
    public Dictionary<string, object>? ExtraBody { get; set; }

    public string? GetEntityId()
    {
        if (ExtraBody?.TryGetValue("entity_id", out var entityId) == true)
        {
            return entityId?.ToString();
        }
        return null;
    }

    public string? GetThreadId()
    {
        if (ExtraBody?.TryGetValue("thread_id", out var threadId) == true)
        {
            return threadId?.ToString();
        }
        return null;
    }

    public string GetLastMessageContent()
    {
        // First try input field (Python backend format)
        if (!string.IsNullOrEmpty(Input))
        {
            return Input;
        }

        // Fallback to messages
        return Messages?.LastOrDefault()?.Content ?? "";
    }

    public ChatMessage[] ToChatMessages()
    {
        var content = GetLastMessageContent();
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        return [new ChatMessage(ChatRole.User, content)];
    }
}

public class DevUIRequestMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}