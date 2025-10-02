using Microsoft.Agents.AI.DevUI.Models.Tracing;

namespace Microsoft.Agents.AI.DevUI.Core;

/// <summary>
/// Provides tracing and diagnostics for agent/workflow execution
/// </summary>
public interface ITracingService
{
    /// <summary>
    /// Start tracing for a session
    /// </summary>
    Task<string> StartTracingAsync(string sessionId, string entityId);

    /// <summary>
    /// Record a trace event
    /// </summary>
    Task RecordTraceEventAsync(string sessionId, TraceEvent traceEvent);

    /// <summary>
    /// Stop tracing for a session
    /// </summary>
    Task StopTracingAsync(string sessionId);

    /// <summary>
    /// Get trace events for a session
    /// </summary>
    Task<IEnumerable<TraceEvent>> GetTraceEventsAsync(string sessionId);

    /// <summary>
    /// Get trace summary for a session
    /// </summary>
    Task<TraceSummary> GetTraceSummaryAsync(string sessionId);
}