using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.DevUI.Models.Execution;

/// <summary>
/// OpenAI Responses API event types - matches Python openai.types.responses exactly
/// These are the streaming event types sent via SSE
/// </summary>

/// <summary>
/// Text delta event - maps to Python's ResponseTextDeltaEvent from openai.types.responses
/// </summary>
public class ResponseTextDeltaEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response.output_text.delta";

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("content_index")]
    public int ContentIndex { get; set; }

    [JsonPropertyName("delta")]
    public string Delta { get; set; } = "";

    [JsonPropertyName("sequence_number")]
    public int SequenceNumber { get; set; }

    [JsonPropertyName("logprobs")]
    public List<object> Logprobs { get; set; } = new();
}

/// <summary>
/// Function call arguments delta event - maps to Python's ResponseFunctionCallArgumentsDeltaEvent
/// </summary>
public class ResponseFunctionCallArgumentsDeltaEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response.function_call_arguments.delta";

    [JsonPropertyName("delta")]
    public string Delta { get; set; } = "";

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("sequence_number")]
    public int SequenceNumber { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }
}

/// <summary>
/// Usage event complete - maps to Python's ResponseUsageEventComplete
/// </summary>
public class ResponseUsageEventComplete
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response.usage.complete";

    [JsonPropertyName("delta")]
    public object? Delta { get; set; }

    [JsonPropertyName("sequence_number")]
    public int SequenceNumber { get; set; }

    [JsonPropertyName("data")]
    public ResponseUsageData? Data { get; set; }

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }
}

public class ResponseUsageData
{
    [JsonPropertyName("usage_data")]
    public UsageDetails? UsageDetails { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";
}

public class UsageDetails
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "usage_details";

    [JsonPropertyName("input_token_count")]
    public int InputTokenCount { get; set; }

    [JsonPropertyName("output_token_count")]
    public int OutputTokenCount { get; set; }

    [JsonPropertyName("total_token_count")]
    public int TotalTokenCount { get; set; }
}

/// <summary>
/// Function result complete event
/// </summary>
public class ResponseFunctionResultComplete
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response.function_result.complete";

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }

    [JsonPropertyName("call_id")]
    public string CallId { get; set; } = "";

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("sequence_number")]
    public int SequenceNumber { get; set; }
}

/// <summary>
/// Workflow event complete - custom Agent Framework extension
/// </summary>
public class ResponseWorkflowEventComplete
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response.workflow_event.complete";

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }

    [JsonPropertyName("executor_id")]
    public string? ExecutorId { get; set; }

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("sequence_number")]
    public int SequenceNumber { get; set; }
}
