using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.DevUI.Models;

public class EntityInfo
{
    // Always present (core entity data)
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "agent-framework";

    [JsonPropertyName("tools")]
    public List<object>? Tools { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Workflow-specific fields (populated only for detailed info requests)
    [JsonPropertyName("executors")]
    public List<string>? Executors { get; set; }

    [JsonPropertyName("workflow_dump")]
    public Dictionary<string, object>? WorkflowDump { get; set; }

    [JsonPropertyName("input_schema")]
    public Dictionary<string, object>? InputSchema { get; set; }

    [JsonPropertyName("input_type_name")]
    public string? InputTypeName { get; set; }

    [JsonPropertyName("start_executor_id")]
    public string? StartExecutorId { get; set; }
}

public class DiscoveryResponse
{
    [JsonPropertyName("entities")]
    public List<EntityInfo> Entities { get; set; } = new();
}