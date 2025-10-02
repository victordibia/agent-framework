using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.DevUI.Models.Execution;

/// <summary>
/// DevUI execution request model compatible with OpenAI format
/// </summary>
public class DevUIExecutionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "agent-framework";

    [JsonPropertyName("input")]
    public string? Input { get; set; }  // Can be string or messages - for now support string

    [JsonPropertyName("messages")]
    public List<RequestMessage>? Messages { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("extra_body")]
    public Dictionary<string, object> ExtraBody { get; set; } = new();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 1.0;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1000;

    /// <summary>
    /// Get entity ID from extra_body
    /// </summary>
    public string? GetEntityId()
    {
        if (ExtraBody.TryGetValue("entity_id", out var entityId))
        {
            return entityId?.ToString();
        }
        return null;
    }

    /// <summary>
    /// Get last message content (supports both input string and messages array)
    /// </summary>
    public string GetLastMessageContent()
    {
        // Prefer Input if available (OpenAI Responses API format)
        if (!string.IsNullOrEmpty(Input))
        {
            return Input;
        }

        // Fall back to Messages (Chat Completions API format)
        return Messages?.LastOrDefault()?.Content ?? "";
    }

    /// <summary>
    /// Convert to ChatMessage array for Agent Framework
    /// </summary>
    public ChatMessage[] ToChatMessages()
    {
        // If Input is provided (Responses API format), create a single user message
        if (!string.IsNullOrEmpty(Input))
        {
            return new[] { new ChatMessage(ChatRole.User, Input) };
        }

        // Otherwise use Messages array (Chat Completions format)
        if (Messages != null && Messages.Any())
        {
            return Messages.Select(m => new ChatMessage(
                m.Role == "user" ? ChatRole.User :
                m.Role == "assistant" ? ChatRole.Assistant :
                ChatRole.System,
                m.Content)).ToArray();
        }

        return Array.Empty<ChatMessage>();
    }
}

/// <summary>
/// Request message
/// </summary>
public class RequestMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

/// <summary>
/// Execution status information
/// </summary>
public class ExecutionStatus
{
    public string SessionId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public ExecutionState State { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Execution states
/// </summary>
public enum ExecutionState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}