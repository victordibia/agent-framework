using Microsoft.Agents.AI.DevUI.Models.Execution;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

namespace Microsoft.Agents.AI.DevUI.Core;

/// <summary>
/// Maps Agent Framework messages/events to OpenAI-compatible format
/// </summary>
public interface IMessageMapper
{
    /// <summary>
    /// Convert Agent Framework event to OpenAI streaming events
    /// </summary>
    Task<IEnumerable<object>> ConvertEventAsync(object rawEvent, DevUIExecutionRequest request, string sessionId);

    /// <summary>
    /// Convert Agent Framework messages to OpenAI chat completion format
    /// </summary>
    object CreateChatCompletion(DevUIExecutionRequest request, string content, string? sessionId = null);

    /// <summary>
    /// Convert Agent Framework error to OpenAI error format
    /// </summary>
    object CreateErrorResponse(DevUIExecutionRequest request, string errorMessage, string? sessionId = null);

    /// <summary>
    /// Convert Agent Framework streaming response to OpenAI streaming chunk
    /// </summary>
    object CreateStreamingChunk(DevUIExecutionRequest request, string content, bool isComplete = false, string? sessionId = null);
}