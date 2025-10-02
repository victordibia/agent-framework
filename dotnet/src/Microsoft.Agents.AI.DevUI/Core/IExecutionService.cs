using Microsoft.Agents.AI.DevUI.Models.Execution;

namespace Microsoft.Agents.AI.DevUI.Core;

/// <summary>
/// Executes Agent Framework entities (agents and workflows)
/// </summary>
public interface IExecutionService
{
    /// <summary>
    /// Execute entity and return simple response (non-streaming)
    /// </summary>
    Task<object> ExecuteEntityAsync(string entityId, DevUIExecutionRequest request, string? sessionId = null);

    /// <summary>
    /// Execute entity with streaming support
    /// </summary>
    IAsyncEnumerable<object> ExecuteEntityStreamingAsync(string entityId, DevUIExecutionRequest request, string? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if entity can be executed
    /// </summary>
    Task<bool> CanExecuteEntityAsync(string entityId);

    /// <summary>
    /// Get execution status for a session
    /// </summary>
    Task<ExecutionStatus> GetExecutionStatusAsync(string sessionId);
}