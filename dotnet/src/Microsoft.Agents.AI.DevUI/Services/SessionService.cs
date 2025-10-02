using Microsoft.Agents.AI.DevUI.Core;
using Microsoft.Agents.AI.DevUI.Models.Session;
using System.Collections.Concurrent;

namespace Microsoft.Agents.AI.DevUI.Services;

/// <summary>
/// Manages execution sessions for tracking requests and context
/// Equivalent to Python _session.py
/// </summary>
public class SessionService : ISessionService
{
    private readonly ConcurrentDictionary<string, SessionData> _sessions = new();
    private readonly ILogger<SessionService> _logger;

    public SessionService(ILogger<SessionService> logger)
    {
        _logger = logger;
    }

    public Task<string> CreateSessionAsync(string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString();

        var sessionData = new SessionData
        {
            Id = sessionId,
            CreatedAt = DateTime.UtcNow,
            Active = true
        };

        _sessions[sessionId] = sessionData;
        _logger.LogDebug("Created session: {SessionId}", sessionId);

        return Task.FromResult(sessionId);
    }

    public Task<SessionData?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task RecordRequestAsync(string sessionId, RequestRecord request)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Requests.Add(request);
            _logger.LogDebug("Recorded request {RequestId} in session {SessionId}", request.Id, sessionId);
        }
        else
        {
            _logger.LogWarning("Attempted to record request in non-existent session: {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task RecordResponseAsync(string sessionId, ResponseRecord response)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Responses.Add(response);
            _logger.LogDebug("Recorded response {ResponseId} in session {SessionId}", response.Id, sessionId);
        }
        else
        {
            _logger.LogWarning("Attempted to record response in non-existent session: {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task<SessionSummary> GetSessionSummaryAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            var summary = new SessionSummary
            {
                Id = session.Id,
                CreatedAt = session.CreatedAt,
                Active = session.Active,
                RequestCount = session.Requests.Count,
                ResponseCount = session.Responses.Count,
                LastActivity = session.Responses.LastOrDefault()?.Timestamp ?? session.Requests.LastOrDefault()?.Timestamp,
                EntityIds = session.Requests.Select(r => r.EntityId).Distinct().ToList()
            };

            return Task.FromResult(summary);
        }

        return Task.FromResult(new SessionSummary { Id = sessionId });
    }

    public Task CloseSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Active = false;
            _logger.LogDebug("Closed session: {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<SessionSummary>> ListActiveSessionsAsync()
    {
        var activeSessions = _sessions.Values
            .Where(s => s.Active)
            .Select(s => new SessionSummary
            {
                Id = s.Id,
                CreatedAt = s.CreatedAt,
                Active = s.Active,
                RequestCount = s.Requests.Count,
                ResponseCount = s.Responses.Count,
                LastActivity = s.Responses.LastOrDefault()?.Timestamp ?? s.Requests.LastOrDefault()?.Timestamp,
                EntityIds = s.Requests.Select(r => r.EntityId).Distinct().ToList()
            });

        return Task.FromResult(activeSessions);
    }

    /// <summary>
    /// Cleanup old inactive sessions (can be called periodically)
    /// </summary>
    public Task CleanupOldSessionsAsync(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var oldSessions = _sessions.Where(kvp =>
            !kvp.Value.Active && kvp.Value.CreatedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in oldSessions)
        {
            _sessions.TryRemove(sessionId, out _);
            _logger.LogDebug("Cleaned up old session: {SessionId}", sessionId);
        }

        _logger.LogInformation("Cleaned up {Count} old sessions", oldSessions.Count);
        return Task.CompletedTask;
    }
}