namespace Microsoft.Agents.AI.DevUI.Models.Tracing;

/// <summary>
/// Trace event for debugging
/// </summary>
public class TraceEvent
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public TraceEventType Type { get; set; }
    public string Name { get; set; } = "";
    public Dictionary<string, object> Data { get; set; } = new();
    public TimeSpan? Duration { get; set; }
    public string? ParentId { get; set; }
}

/// <summary>
/// Types of trace events
/// </summary>
public enum TraceEventType
{
    AgentStart,
    AgentComplete,
    WorkflowStart,
    WorkflowComplete,
    ExecutorStart,
    ExecutorComplete,
    Message,
    Error,
    Custom
}

/// <summary>
/// Trace summary for a session
/// </summary>
public class TraceSummary
{
    public string SessionId { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int EventCount { get; set; }
    public Dictionary<TraceEventType, int> EventCountsByType { get; set; } = new();
    public List<string> EntityIds { get; set; } = new();
    public bool HasErrors { get; set; }
}