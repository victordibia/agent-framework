using Microsoft.Agents.AI.DevUI.Models.Session;

namespace Microsoft.Agents.AI.DevUI.Core;

/// <summary>
/// Manages execution sessions for tracking requests and context
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Create a new execution session
    /// </summary>
    Task<string> CreateSessionAsync(string? sessionId = null);

    /// <summary>
    /// Get session information
    /// </summary>
    Task<SessionData?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Record a request in the session
    /// </summary>
    Task RecordRequestAsync(string sessionId, RequestRecord request);

    /// <summary>
    /// Record a response in the session
    /// </summary>
    Task RecordResponseAsync(string sessionId, ResponseRecord response);

    /// <summary>
    /// Get session summary
    /// </summary>
    Task<SessionSummary> GetSessionSummaryAsync(string sessionId);

    /// <summary>
    /// Close a session
    /// </summary>
    Task CloseSessionAsync(string sessionId);

    /// <summary>
    /// List all active sessions
    /// </summary>
    Task<IEnumerable<SessionSummary>> ListActiveSessionsAsync();
}