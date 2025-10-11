using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.DevUI.Models;

/// <summary>
/// Entity information model matching Python's EntityInfo
/// </summary>
public class EntityInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";  // "agent" or "workflow"

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "agent-framework";

    [JsonPropertyName("tools")]
    public List<object> Tools { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Additional fields (can be populated via discovery enrichment)
    [JsonPropertyName("source")]
    public string? Source { get; set; }  // "directory", "in_memory", "remote_gallery"

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("chat_client_type")]
    public string? ChatClientType { get; set; }

    // Workflow-specific fields
    [JsonPropertyName("executors")]
    public List<string>? Executors { get; set; }

    [JsonPropertyName("workflow_dump")]
    public Dictionary<string, object>? WorkflowDump { get; set; }

    [JsonPropertyName("input_schema")]
    public Dictionary<string, object>? InputSchema { get; set; }

    [JsonPropertyName("start_executor_id")]
    public string? StartExecutorId { get; set; }

    [JsonPropertyName("input_type_name")]
    public string? InputTypeName { get; set; }
}

/// <summary>
/// Discovery response wrapping list of entities
/// </summary>
public class DiscoveryResponse
{
    [JsonPropertyName("entities")]
    public List<EntityInfo> Entities { get; set; } = new();
}
