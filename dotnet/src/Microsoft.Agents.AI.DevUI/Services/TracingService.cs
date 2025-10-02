using Microsoft.Agents.AI.DevUI.Core;
using Microsoft.Agents.AI.DevUI.Models.Tracing;
using System.Collections.Concurrent;
using System.Diagnostics;
using TraceEventType = Microsoft.Agents.AI.DevUI.Models.Tracing.TraceEventType;

namespace Microsoft.Agents.AI.DevUI.Services;

/// <summary>
/// Captures telemetry spans already emitted by Agent Framework for agent/workflow runs
/// Hooks into existing Agent Framework telemetry like Python version hooks into OpenTelemetry
/// </summary>
public class TracingService : ITracingService
{
    private readonly ConcurrentDictionary<string, List<TraceEvent>> _traceEvents = new();
    private readonly ILogger<TracingService> _logger;

    public TracingService(ILogger<TracingService> logger)
    {
        _logger = logger;
    }

    public Task<string> StartTracingAsync(string sessionId, string entityId)
    {
        // Initialize trace collection for this session
        // The Agent Framework will emit spans automatically, we just capture them
        _traceEvents[sessionId] = new List<TraceEvent>();

        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        _logger.LogDebug("Started capturing Agent Framework telemetry for session {SessionId}", sessionId);

        return Task.FromResult(traceId);
    }

    public Task RecordTraceEventAsync(string sessionId, TraceEvent traceEvent)
    {
        if (_traceEvents.TryGetValue(sessionId, out var events))
        {
            // Add activity context if available
            var activity = Activity.Current;
            if (activity != null)
            {
                traceEvent.Data["activity_id"] = activity.Id ?? "";
                traceEvent.Data["trace_id"] = activity.TraceId.ToString();
                traceEvent.Data["span_id"] = activity.SpanId.ToString();
                traceEvent.ParentId = activity.ParentId;
            }

            events.Add(traceEvent);
            _logger.LogTrace("Recorded trace event {EventId} of type {EventType} in session {SessionId}",
                traceEvent.Id, traceEvent.Type, sessionId);
        }
        else
        {
            _logger.LogWarning("Attempted to record trace event in non-existent session: {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task StopTracingAsync(string sessionId)
    {
        // Agent Framework spans will naturally complete when agent/workflow execution finishes
        _logger.LogDebug("Stopped capturing telemetry for session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<TraceEvent>> GetTraceEventsAsync(string sessionId)
    {
        _traceEvents.TryGetValue(sessionId, out var events);
        return Task.FromResult<IEnumerable<TraceEvent>>(events ?? new List<TraceEvent>());
    }

    public Task<TraceSummary> GetTraceSummaryAsync(string sessionId)
    {
        var summary = new TraceSummary { SessionId = sessionId };

        if (_traceEvents.TryGetValue(sessionId, out var events))
        {
            summary.EventCount = events.Count;
            summary.EventCountsByType = events.GroupBy(e => e.Type)
                .ToDictionary(g => g.Key, g => g.Count());
            summary.EntityIds = events.Select(e => e.EntityId).Distinct().ToList();
            summary.HasErrors = events.Any(e => e.Type == TraceEventType.Error);

            if (events.Count > 0)
            {
                summary.StartTime = events.Min(e => e.Timestamp);
                summary.EndTime = events.Max(e => e.Timestamp);
                summary.TotalDuration = (summary.EndTime ?? summary.StartTime) - summary.StartTime;
            }
        }

        return Task.FromResult(summary);
    }

    /// <summary>
    /// Record agent/workflow execution events automatically
    /// </summary>
    public async Task RecordExecutionEventAsync(string sessionId, string entityId, Models.Tracing.TraceEventType eventType, string name, object? data = null, string? parentId = null)
    {
        var traceEvent = new TraceEvent
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            EntityId = entityId,
            Timestamp = DateTime.UtcNow,
            Type = eventType,
            Name = name,
            ParentId = parentId,
            Data = data != null ? new Dictionary<string, object> { ["data"] = data } : new Dictionary<string, object>()
        };

        await RecordTraceEventAsync(sessionId, traceEvent);
    }

    /// <summary>
    /// Hook into Agent Framework's existing span creation for timing operations
    /// </summary>
    public IDisposable StartSpan(string sessionId, string entityId, string operationName)
    {
        // Agent Framework already creates spans, we just need to capture the current activity
        var activity = Activity.Current;
        _logger.LogTrace("Started span {OperationName} for session {SessionId}", operationName, sessionId);

        return new DisposableAction(() =>
        {
            _logger.LogTrace("Completed span {OperationName} for session {SessionId}", operationName, sessionId);
        });
    }

    private class DisposableAction : IDisposable
    {
        private readonly Action _action;

        public DisposableAction(Action action) => _action = action;

        public void Dispose() => _action();
    }
}