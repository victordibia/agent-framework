using Microsoft.Agents.AI.DevUI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using System.Text.Json;
using System.Collections.Concurrent;
using ResponseEvents = Microsoft.Agents.AI.DevUI.Models.Execution;

namespace Microsoft.Agents.AI.DevUI.Services;

/// <summary>
/// Maps Agent Framework messages/events to OpenAI Response format (NOT chat completion)
/// Matches Python DevUI _mapper.py implementation exactly
/// </summary>
public class MessageMapperService
{
    private readonly ILogger<MessageMapperService> _logger;
    private readonly ConcurrentDictionary<string, ConversionContext> _contexts = new();

    public MessageMapperService(ILogger<MessageMapperService> logger)
    {
        _logger = logger;
    }

    private class ConversionContext
    {
        public int SequenceCounter { get; set; }
        public string ItemId { get; set; } = $"msg_{Guid.NewGuid().ToString("N")[..8]}";
        public int ContentIndex { get; set; }
        public int OutputIndex { get; set; }
        public Dictionary<string, FunctionCallInfo> ActiveFunctionCalls { get; set; } = new();
    }

    private class FunctionCallInfo
    {
        public required string ItemId { get; set; }
        public required string Name { get; set; }
        public required List<string> ArgumentsChunks { get; set; }
    }

    private ConversionContext GetOrCreateContext(string sessionId)
    {
        return _contexts.GetOrAdd(sessionId, _ => new ConversionContext());
    }

    private int NextSequence(ConversionContext context)
    {
        return ++context.SequenceCounter;
    }

    public async Task<IEnumerable<object>> ConvertEventAsync(object rawEvent, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, string sessionId)
    {
        var context = GetOrCreateContext(sessionId);

        try
        {
            // Handle Agent Framework events
            if (rawEvent is AgentRunResponseUpdate agentUpdate)
            {
                return ConvertAgentUpdate(agentUpdate, context);
            }

            // Handle Workflow events
            if (rawEvent is WorkflowEvent workflowEvent)
            {
                return ConvertWorkflowEvent(workflowEvent, context);
            }

            // Handle ChatMessage (fallback)
            if (rawEvent is ChatMessage chatMessage)
            {
                return ConvertChatMessage(chatMessage, context);
            }

            // Unknown event type
            _logger.LogWarning("Unknown event type: {Type}", rawEvent.GetType().Name);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting event");
            return [CreateErrorEvent(ex.Message, context)];
        }
    }

    /// <summary>
    /// Convert AgentRunResponseUpdate to OpenAI Response stream events
    /// Maps: TextContent -> response.output_text.delta
    ///       FunctionCallContent -> response.function_call_arguments.delta
    ///       FunctionResultContent -> response.function_result.complete
    ///       etc.
    /// </summary>
    private List<object> ConvertAgentUpdate(AgentRunResponseUpdate update, ConversionContext context)
    {
        var events = new List<object>();

        if (update.Contents == null || !update.Contents.Any())
        {
            return events;
        }

        foreach (var content in update.Contents)
        {
            try
            {
                var contentEvents = content switch
                {
                    TextContent text => MapTextContent(text, context),
                    FunctionCallContent funcCall => MapFunctionCallContent(funcCall, context),
                    FunctionResultContent funcResult => MapFunctionResultContent(funcResult, context),
                    UsageContent usage => MapUsageContent(usage, context),
                    ErrorContent error => MapErrorContent(error, context),
                    DataContent data => MapDataContent(data, context),
                    UriContent uri => MapUriContent(uri, context),
                    _ => MapUnknownContent(content, context)
                };

                events.AddRange(contentEvents);
                context.ContentIndex++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error mapping content type {Type}", content.GetType().Name);
                events.Add(CreateErrorEvent($"Content mapping failed: {ex.Message}", context));
            }
        }

        return events;
    }

    /// <summary>
    /// Map TextContent to response.output_text.delta event (Python's ResponseTextDeltaEvent)
    /// </summary>
    private List<object> MapTextContent(TextContent content, ConversionContext context)
    {
        var text = content.Text ?? string.Empty;

        // Create one event per TextContent chunk (Agent Framework already streams word-by-word)
        var events = new List<object>
        {
            new ResponseEvents.ResponseTextDeltaEvent
            {
                ItemId = context.ItemId,
                OutputIndex = context.OutputIndex,
                ContentIndex = context.ContentIndex,
                Delta = text,
                SequenceNumber = NextSequence(context),
                Logprobs = new List<object>()
            }
        };

        return events;
    }

    /// <summary>
    /// Map FunctionCallContent to OpenAI events following Responses API spec
    ///
    /// Agent Framework emits FunctionCallContent in two patterns:
    /// 1. First event: call_id + name + empty/no arguments
    /// 2. Subsequent events: empty call_id/name + argument chunks
    ///
    /// We emit:
    /// 1. response.output_item.added (with full metadata) for the first event
    /// 2. response.function_call_arguments.delta (referencing item_id) for chunks
    /// </summary>
    private List<object> MapFunctionCallContent(FunctionCallContent content, ConversionContext context)
    {
        var events = new List<object>();

        // CASE 1: New function call (has call_id and name)
        // This is the first event that establishes the function call
        if (!string.IsNullOrEmpty(content.CallId) && !string.IsNullOrEmpty(content.Name))
        {
            // Track this function call for later argument deltas
            if (!context.ActiveFunctionCalls.ContainsKey(content.CallId))
            {
                context.ActiveFunctionCalls[content.CallId] = new FunctionCallInfo
                {
                    ItemId = content.CallId,
                    Name = content.Name,
                    ArgumentsChunks = new List<string>()
                };

                // Emit response.output_item.added event per OpenAI spec
                events.Add(new
                {
                    type = "response.output_item.added",
                    item = new
                    {
                        id = content.CallId,
                        call_id = content.CallId,
                        name = content.Name,
                        arguments = "",  // Empty initially, will be filled by deltas
                        type = "function_call",
                        status = "in_progress"
                    },
                    output_index = context.OutputIndex,
                    sequence_number = NextSequence(context)
                });
            }
        }

        // CASE 2: Argument deltas (content has arguments)
        if (content.Arguments != null)
        {
            // Find the active function call for these arguments
            var activeCall = GetActiveFunctionCall(content, context);

            if (activeCall != null)
            {
                // Serialize arguments to JSON
                var argsJson = JsonSerializer.Serialize(content.Arguments);

                // Chunk JSON string for streaming (like Python does)
                foreach (var chunk in ChunkJsonString(argsJson))
                {
                    events.Add(new
                    {
                        type = "response.function_call_arguments.delta",
                        delta = chunk,
                        item_id = activeCall.ItemId,
                        output_index = context.OutputIndex,
                        sequence_number = NextSequence(context)
                    });

                    // Track chunk for debugging
                    activeCall.ArgumentsChunks.Add(chunk);
                }
            }
        }

        return events;
    }

    /// <summary>
    /// Find the active function call for this content
    /// Uses call_id if present, otherwise falls back to most recent call
    /// </summary>
    private FunctionCallInfo? GetActiveFunctionCall(FunctionCallContent content, ConversionContext context)
    {
        // If content has call_id, use it to find the exact call
        if (!string.IsNullOrEmpty(content.CallId) && context.ActiveFunctionCalls.TryGetValue(content.CallId, out var call))
        {
            return call;
        }

        // Otherwise, use the most recent call (last one added)
        if (context.ActiveFunctionCalls.Count > 0)
        {
            return context.ActiveFunctionCalls.Values.Last();
        }

        return null;
    }

    /// <summary>
    /// Map FunctionResultContent to response.function_result.complete event
    /// </summary>
    private List<object> MapFunctionResultContent(FunctionResultContent content, ConversionContext context)
    {
        return
        [
            new
            {
                type = "response.function_result.complete",
                delta = (string?)null,
                sequence_number = NextSequence(context),
                data = new
                {
                    call_id = content.CallId,
                    result = content.Result?.ToString(),
                    status = "completed",
                    exception = (string?)null,
                    timestamp = DateTime.UtcNow.ToString("O")
                },
                call_id = content.CallId,
                item_id = context.ItemId,
                output_index = context.OutputIndex
            }
        ];
    }

    /// <summary>
    /// Map UsageContent to response.usage.complete event
    /// </summary>
    private List<object> MapUsageContent(UsageContent content, ConversionContext context)
    {
        return
        [
            new
            {
                type = "response.usage.complete",
                delta = (string?)null,
                sequence_number = NextSequence(context),
                data = new
                {
                    usage_data = new Dictionary<string, object>(),
                    total_tokens = content.Details?.TotalTokenCount ?? 0,
                    completion_tokens = content.Details?.OutputTokenCount ?? 0,
                    prompt_tokens = content.Details?.InputTokenCount ?? 0,
                    timestamp = DateTime.UtcNow.ToString("O")
                },
                item_id = context.ItemId,
                output_index = context.OutputIndex
            }
        ];
    }

    /// <summary>
    /// Map ErrorContent to response.error event
    /// </summary>
    private List<object> MapErrorContent(ErrorContent content, ConversionContext context)
    {
        return
        [
            new
            {
                type = "error",
                message = content.Message ?? "Unknown error",
                code = (string?)null,
                param = (string?)null,
                sequence_number = NextSequence(context)
            }
        ];
    }

    /// <summary>
    /// Convert WorkflowEvent to response.workflow_event.complete
    /// </summary>
    public object ConvertWorkflowEvent(WorkflowEvent workflowEvent, string sessionId, int sequenceNumber)
    {
        var itemId = $"msg_{Guid.NewGuid().ToString()[..8]}";

        // Extract executor_id if this is an ExecutorEvent
        string? executorId = null;
        if (workflowEvent is ExecutorEvent execEvent)
        {
            executorId = execEvent.ExecutorId;
        }

        return new ResponseEvents.ResponseWorkflowEventComplete
        {
            Type = "response.workflow_event.complete",
            Data = new Dictionary<string, object>
            {
                ["event_type"] = workflowEvent.GetType().Name,
                ["data"] = workflowEvent.Data ?? new object(),
                ["executor_id"] = executorId ?? string.Empty,
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            },
            ExecutorId = executorId,
            ItemId = itemId,
            OutputIndex = 0,
            SequenceNumber = sequenceNumber
        };
    }

    /// <summary>
    /// Map DataContent/UriContent to response.trace.complete event
    /// </summary>
    private List<object> MapDataContent(DataContent content, ConversionContext context)
    {
        return
        [
            new
            {
                type = "response.trace.complete",
                data = new
                {
                    content_type = "DataContent",
                    data = System.Text.Encoding.UTF8.GetString(content.Data.ToArray())
                },
                item_id = context.ItemId,
                sequence_number = NextSequence(context)
            }
        ];
    }

    private List<object> MapUriContent(UriContent content, ConversionContext context)
    {
        return
        [
            new
            {
                type = "response.trace.complete",
                data = new
                {
                    content_type = "UriContent",
                    uri = content.Uri?.ToString()
                },
                item_id = context.ItemId,
                sequence_number = NextSequence(context)
            }
        ];
    }

    private List<object> MapUnknownContent(AIContent content, ConversionContext context)
    {
        return
        [
            new
            {
                type = "response.trace.complete",
                data = new
                {
                    content_type = content.GetType().Name,
                    raw = content.ToString()
                },
                item_id = context.ItemId,
                sequence_number = NextSequence(context)
            }
        ];
    }

    /// <summary>
    /// Convert WorkflowEvent to response.workflow_event.complete
    /// </summary>
    private List<object> ConvertWorkflowEvent(WorkflowEvent workflowEvent, ConversionContext context)
    {
        // Try to get ExecutorId if it's an ExecutorEvent
        string? executorId = null;
        if (workflowEvent is ExecutorEvent executorEvent)
        {
            executorId = executorEvent.ExecutorId;
        }

        return
        [
            new
            {
                type = "response.workflow_event.complete",
                delta = (string?)null,
                sequence_number = NextSequence(context),
                data = new
                {
                    event_type = workflowEvent.GetType().Name,
                    data = workflowEvent.Data?.ToString(),
                    executor_id = executorId,
                    timestamp = DateTime.UtcNow.ToString("O")
                },
                executor_id = executorId,
                item_id = context.ItemId,
                output_index = context.OutputIndex
            }
        ];
    }

    /// <summary>
    /// Fallback for ChatMessage conversion
    /// </summary>
    private List<object> ConvertChatMessage(ChatMessage message, ConversionContext context)
    {
        var text = message.Text ?? string.Empty;
        var events = new List<object>();

        foreach (var ch in text)
        {
            events.Add(new
            {
                type = "response.output_text.delta",
                delta = ch.ToString(),
                content_index = context.ContentIndex,
                item_id = context.ItemId,
                output_index = context.OutputIndex,
                sequence_number = NextSequence(context),
                logprobs = Array.Empty<object>()
            });
        }

        return events;
    }

    private object CreateErrorEvent(string message, ConversionContext context)
    {
        return new
        {
            type = "error",
            message,
            code = (string?)null,
            param = (string?)null,
            sequence_number = NextSequence(context)
        };
    }

    /// <summary>
    /// Chunk JSON string for streaming (matches Python implementation)
    /// </summary>
    private static IEnumerable<string> ChunkJsonString(string json)
    {
        // Simple chunking - can be made more sophisticated
        var chunkSize = 10;
        for (int i = 0; i < json.Length; i += chunkSize)
        {
            yield return json.Substring(i, Math.Min(chunkSize, json.Length - i));
        }
    }

    public object CreateChatCompletion(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, string content, string? sessionId = null)
    {
        // This is for non-streaming final response
        // Create OpenAI Response format (NOT chat.completion)
        return new
        {
            id = $"resp_{Guid.NewGuid().ToString("N")[..12]}",
            @object = "response",
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = request.Model,
            output = new[]
            {
                new
                {
                    type = "message",
                    role = "assistant",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = content,
                            annotations = Array.Empty<object>()
                        }
                    },
                    id = $"msg_{Guid.NewGuid().ToString("N")[..8]}",
                    status = "completed"
                }
            },
            usage = new
            {
                input_tokens = request.GetLastMessageContent().Length / 4,
                output_tokens = content.Length / 4,
                total_tokens = (request.GetLastMessageContent().Length / 4) + (content.Length / 4),
                input_tokens_details = new { cached_tokens = 0 },
                output_tokens_details = new { reasoning_tokens = 0 }
            },
            parallel_tool_calls = false,
            tool_choice = "none",
            tools = Array.Empty<object>()
        };
    }

    public object CreateErrorResponse(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, string errorMessage, string? sessionId = null)
    {
        var context = GetOrCreateContext(sessionId ?? Guid.NewGuid().ToString());
        return CreateErrorEvent(errorMessage, context);
    }

    public object CreateStreamingChunk(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, string content, bool isComplete = false, string? sessionId = null)
    {
        var context = GetOrCreateContext(sessionId ?? Guid.NewGuid().ToString());

        // Create response.output_text.delta event
        return new
        {
            type = "response.output_text.delta",
            delta = content,
            content_index = context.ContentIndex,
            item_id = context.ItemId,
            output_index = context.OutputIndex,
            sequence_number = NextSequence(context),
            logprobs = Array.Empty<object>()
        };
    }
}