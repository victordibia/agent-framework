namespace Microsoft.Agents.AI.DevUI.Models.Session;

/// <summary>
/// Session data container
/// </summary>
public class SessionData
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool Active { get; set; }
    public List<RequestRecord> Requests { get; set; } = new();
    public List<ResponseRecord> Responses { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
}

/// <summary>
/// Request record for session tracking
/// </summary>
public class RequestRecord
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string EntityId { get; set; } = "";
    public string Method { get; set; } = "";
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Response record for session tracking
/// </summary>
public class ResponseRecord
{
    public string Id { get; set; } = "";
    public string RequestId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int StatusCode { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Session summary information
/// </summary>
public class SessionSummary
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivity { get; set; }
    public bool Active { get; set; }
    public int RequestCount { get; set; }
    public int ResponseCount { get; set; }
    public List<string> EntityIds { get; set; } = new();
}