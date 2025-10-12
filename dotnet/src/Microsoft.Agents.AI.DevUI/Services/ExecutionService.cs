using Microsoft.Agents.AI.DevUI.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI.Workflows;
using System.Runtime.CompilerServices;
using ExecutionModels = Microsoft.Agents.AI.DevUI.Models.Execution;

namespace Microsoft.Agents.AI.DevUI.Services;

/// <summary>
/// Unified execution service that handles both agents and workflows
/// with real execution and proper OpenAI format mapping
/// </summary>
public class ExecutionService
{
    private readonly EntityDiscoveryService _discoveryService;
    private readonly MessageMapperService _mapperService;
    private readonly ConversationService _conversationService;
    private readonly ILogger<ExecutionService> _logger;

    public ExecutionService(EntityDiscoveryService discoveryService, MessageMapperService mapperService, ConversationService conversationService, ILogger<ExecutionService> logger)
    {
        _discoveryService = discoveryService;
        _mapperService = mapperService;
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>
    /// Execute entity and return simple response (non-streaming)
    /// </summary>
    public async Task<object> ExecuteEntityAsync(string entityId, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request)
    {
        var entityInfo = _discoveryService.GetEntityInfo(entityId);
        if (entityInfo == null)
        {
            throw new InvalidOperationException($"Entity '{entityId}' not found");
        }

        _logger.LogInformation("Executing entity {EntityId}", entityId);

        if (entityInfo.Type == "agent")
        {
            return await ExecuteAgentAsync(entityId, request);
        }
        else
        {
            return await ExecuteWorkflowAsync(entityId, request);
        }
    }

    /// <summary>
    /// Execute entity with streaming support
    /// </summary>
    public async IAsyncEnumerable<object> ExecuteEntityStreamingAsync(string entityId, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, AgentThread? thread = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entityInfo = _discoveryService.GetEntityInfo(entityId);
        if (entityInfo == null)
        {
            throw new InvalidOperationException($"Entity '{entityId}' not found");
        }

        _logger.LogInformation("Executing entity {EntityId} with streaming", entityId);

        if (entityInfo.Type == "agent")
        {
            await foreach (var result in ExecuteAgentStreamingAsync(entityId, request, thread, cancellationToken))
            {
                yield return result;
            }
        }
        else
        {
            // Execute workflow with real streaming support
            await foreach (var result in ExecuteWorkflowStreamingAsync(entityId, request, cancellationToken))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Execute agent with streaming support
    /// </summary>
    public async IAsyncEnumerable<object> ExecuteAgentStreamingAsync(string entityId, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, AgentThread? thread = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Get the actual agent instance
        var agent = _discoveryService.GetEntityObject(entityId) as AIAgent;
        if (agent == null)
        {
            var errorEvent = new
            {
                type = "error",
                error = new
                {
                    message = $"Agent '{entityId}' not found or not accessible",
                    type = "entity_not_found"
                }
            };
            yield return errorEvent;
            yield break;
        }

        // Check if there's a conversation_id in the request
        var conversationId = request.GetConversationId();
        if (!string.IsNullOrEmpty(conversationId) && thread == null)
        {
            // Get or create thread from conversation service
            thread = _conversationService.GetOrCreateThread(conversationId, agent);
            _logger.LogInformation("Using conversation {ConversationId} for agent {AgentId}", conversationId, entityId);
        }

        // Initialize streaming result outside try-catch
        IAsyncEnumerable<AgentRunResponseUpdate>? streamingResult = null;
        Exception? startupError = null;

        // Try to start streaming (with or without thread)
        try
        {
            if (thread != null)
            {
                // When using a thread, pass only the new user input as a string
                // The thread already contains the conversation history
                var userInput = request.GetLastMessageContent();
                _logger.LogInformation("Executing agent {AgentId} with streaming, input: {Input}, thread: true",
                    entityId, userInput);
                streamingResult = agent.RunStreamingAsync(userInput, thread: thread, cancellationToken: cancellationToken);
            }
            else
            {
                // Without a thread, pass the full message history
                var messages = ConvertRequestToMessages(request);
                _logger.LogInformation("Executing agent {AgentId} with streaming, {MessageCount} messages, thread: false",
                    entityId, messages.Length);
                streamingResult = agent.RunStreamingAsync(messages, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            startupError = ex;
        }

        // If startup failed, yield error and exit
        if (startupError != null)
        {
            _logger.LogError(startupError, "Error starting agent execution {AgentId}", entityId);
            var errorEvent = new
            {
                id = Guid.NewGuid().ToString(),
                @object = "error",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                error = new
                {
                    message = $"Agent execution failed: {startupError.Message}",
                    type = "execution_error",
                    code = "agent_execution_failed"
                }
            };
            yield return errorEvent;
            yield break;
        }

        var sessionId = Guid.NewGuid().ToString();  // Same session for all events
        var responseTexts = new List<string>();  // Collect response for conversation history

        // Add user message to conversation if we have a conversation_id
        if (!string.IsNullOrEmpty(conversationId))
        {
            var userInput = request.GetLastMessageContent();
            _conversationService.AddMessage(conversationId, "user", userInput);
        }

        // Process streaming results and convert to OpenAI Responses API events
        await foreach (var update in streamingResult!.WithCancellation(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            // Collect response text for conversation history
            if (!string.IsNullOrEmpty(update.Text))
            {
                responseTexts.Add(update.Text);
            }

            IEnumerable<object>? events = null;
            Exception? conversionError = null;

            try
            {
                events = await _mapperService.ConvertEventAsync(update, request, sessionId);
            }
            catch (Exception ex)
            {
                conversionError = ex;
                _logger.LogError(ex, "Error converting streaming update for agent {AgentId}", entityId);
            }

            // Yield error or events (outside catch block)
            if (conversionError != null)
            {
                yield return new
                {
                    type = "error",
                    message = $"Streaming error: {conversionError.Message}"
                };
                yield break;
            }

            // Yield all events from mapper
            if (events != null)
            {
                foreach (var evt in events)
                {
                    yield return evt;
                }
            }
        }

        // Add assistant response to conversation if we have a conversation_id
        if (!string.IsNullOrEmpty(conversationId) && responseTexts.Count > 0)
        {
            var fullResponse = string.Join("", responseTexts);
            _conversationService.AddMessage(conversationId, "assistant", fullResponse);
        }

        // Stream completes - controller will send [DONE]
    }

    /// <summary>
    /// Execute real agent (non-streaming)
    /// </summary>
    private async Task<object> ExecuteAgentAsync(string entityId, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request)
    {
        // Get the actual agent instance
        var agent = _discoveryService.GetEntityObject(entityId) as AIAgent;
        if (agent == null)
        {
            throw new InvalidOperationException($"Agent '{entityId}' not found or not accessible");
        }

        try
        {
            // Convert request to framework messages
            var messages = ConvertRequestToMessages(request);

            _logger.LogInformation("Executing agent {AgentId} with {MessageCount} messages", entityId, messages.Length);

            // Execute the agent
            var response = await agent.RunAsync(messages);

            // Extract text from response
            var responseText = response.Text ?? "No response text";

            // Convert to OpenAI format
            return CreateSimpleResponse(request, responseText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing agent {AgentId}", entityId);
            return CreateErrorResponse(request, $"Agent execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute workflow with streaming support
    /// </summary>
    public async IAsyncEnumerable<object> ExecuteWorkflowStreamingAsync(string entityId, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Get the actual workflow instance
        var workflow = _discoveryService.GetEntityObject(entityId);
        if (workflow == null)
        {
            var errorEvent = new
            {
                id = Guid.NewGuid().ToString(),
                @object = "error",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                error = new
                {
                    message = $"Workflow '{entityId}' not found or not accessible",
                    type = "entity_not_found",
                    code = "workflow_not_found"
                }
            };
            yield return errorEvent;
            yield break;
        }

        // Convert request to appropriate input
        var inputContent = request.GetLastMessageContent();
        _logger.LogInformation("Executing workflow {WorkflowId} with streaming input: {Input}", entityId, inputContent);

        // Start workflow execution with streaming support
        StreamingRun? streamingRun = null;
        Exception? startupError = null;

        try
        {
            // For workflows that accept string input
            if (workflow is Workflow<string> stringWorkflow)
            {
                streamingRun = await InProcessExecution.StreamAsync(stringWorkflow, inputContent, runId: null, cancellationToken);
            }
            // For workflows that accept ChatMessage[] input
            else if (workflow is Workflow<ChatMessage[]> messageWorkflow)
            {
                var messages = ConvertRequestToMessages(request);
                streamingRun = await InProcessExecution.StreamAsync(messageWorkflow, messages, runId: null, cancellationToken);
            }
            else
            {
                startupError = new InvalidOperationException($"Unsupported workflow input type: {workflow.GetType()}");
            }
        }
        catch (Exception ex)
        {
            startupError = ex;
        }

        // If startup failed, yield error and exit
        if (startupError != null)
        {
            _logger.LogError(startupError, "Error starting workflow execution {WorkflowId}", entityId);
            var errorEvent = new
            {
                id = Guid.NewGuid().ToString(),
                @object = "error",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                error = new
                {
                    message = $"Workflow execution failed: {startupError.Message}",
                    type = "execution_error",
                    code = "workflow_execution_failed"
                }
            };
            yield return errorEvent;
            yield break;
        }

        // Process workflow events in real-time using streaming
        var sessionId = Guid.NewGuid().ToString();
        var sequenceNumber = 0;

        if (streamingRun != null)
        {
            await foreach (var evt in streamingRun.WatchStreamAsync(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                // Convert workflow event to response.workflow_event.complete format
                var workflowEvent = _mapperService.ConvertWorkflowEvent(evt, sessionId, ++sequenceNumber);
                yield return workflowEvent;
            }
        }

        // Stream completes - controller will send [DONE]
    }

    /// <summary>
    /// Execute real workflow
    /// </summary>
    private async Task<object> ExecuteWorkflowAsync(string entityId, Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request)
    {
        // Get the actual workflow instance
        var workflow = _discoveryService.GetEntityObject(entityId);
        if (workflow == null)
        {
            throw new InvalidOperationException($"Workflow '{entityId}' not found or not accessible");
        }

        try
        {
            // Get the input type and create appropriate input
            var workflowType = workflow.GetType();
            var inputContent = request.GetLastMessageContent();

            _logger.LogInformation("Executing workflow {WorkflowId} with input: {Input}", entityId, inputContent);

            // For workflows that accept string input
            if (workflow is Workflow<string> stringWorkflow)
            {
                var run = await InProcessExecution.RunAsync(stringWorkflow, inputContent);
                return await ConvertWorkflowRunToOpenAI(request, run, entityId);
            }
            // For workflows that accept ChatMessage[] input
            else if (workflow is Workflow<ChatMessage[]> messageWorkflow)
            {
                var messages = ConvertRequestToMessages(request);
                var run = await InProcessExecution.RunAsync(messageWorkflow, messages);
                return await ConvertWorkflowRunToOpenAI(request, run, entityId);
            }
            else
            {
                // Fallback for other workflow types
                _logger.LogWarning("Unsupported workflow input type for {WorkflowId}: {Type}", entityId, workflowType);
                return CreateSimpleResponse(request,
                    $"Workflow '{entityId}' executed. Unsupported input type: {workflowType}. Please implement specific input handling.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing workflow {WorkflowId}", entityId);
            return CreateErrorResponse(request, $"Workflow execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert workflow run events to OpenAI format
    /// </summary>
    private async Task<object> ConvertWorkflowRunToOpenAI(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, Run run, string workflowId)
    {
        var responseBuilder = new List<string>();

        // Process all workflow events
        foreach (var evt in run.OutgoingEvents)
        {
            var eventText = ConvertWorkflowEventToText(evt);
            if (!string.IsNullOrEmpty(eventText))
            {
                responseBuilder.Add(eventText);
            }
        }

        // If no meaningful events, create a basic response
        if (responseBuilder.Count == 0)
        {
            var status = await run.GetStatusAsync();
            responseBuilder.Add($"Workflow '{workflowId}' completed with status: {status}");
        }

        var finalResponse = string.Join("\n", responseBuilder);
        return CreateSimpleResponse(request, finalResponse);
    }

    /// <summary>
    /// Convert individual workflow event to text representation
    /// </summary>
    private string ConvertWorkflowEventToText(WorkflowEvent evt)
    {
        return evt switch
        {
            AgentRunResponseEvent responseEvent =>
                $"Agent Response: {responseEvent.Response.Text ?? "No content"}",

            AgentRunUpdateEvent updateEvent =>
                $"Agent Update: {updateEvent.Update.Text ?? "Update"}",

            ExecutorCompletedEvent completedEvent =>
                $"Executor completed: {completedEvent.ExecutorId}",

            WorkflowStartedEvent startedEvent =>
                $"Workflow started: {startedEvent.Data?.ToString() ?? "Processing input"}",

            WorkflowErrorEvent errorEvent =>
                $"Workflow error: {(errorEvent.Data as Exception)?.Message ?? "Unknown error"}",

            WorkflowWarningEvent warningEvent =>
                $"Workflow warning: {warningEvent.Data?.ToString() ?? "Warning occurred"}",

            // ExecutorFailureEvent removed - not available in current framework

            ExecutorInvokedEvent invokedEvent =>
                $"Executor '{invokedEvent.ExecutorId}' invoked: {invokedEvent.Data?.ToString() ?? "Processing"}",

            SuperStepStartedEvent stepStartedEvent =>
                $"Step {stepStartedEvent.StepNumber} started",

            SuperStepCompletedEvent stepCompletedEvent =>
                $"Step {stepCompletedEvent.StepNumber} completed",

            RequestInfoEvent requestEvent =>
                $"External request: {requestEvent.Data?.ToString() ?? "User input required"}",

            _ =>
                $"{evt.GetType().Name}: {evt.Data?.ToString() ?? "No data"}"
        };
    }

    /// <summary>
    /// Convert DevUI request to framework ChatMessage array
    /// </summary>
    private ChatMessage[] ConvertRequestToMessages(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request)
    {
        // Use the improved parsing from the request model
        return request.ToChatMessages();
    }

    /// <summary>
    /// Create simple OpenAI-compatible response
    /// </summary>
    private object CreateSimpleResponse(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, string content)
    {
        return new
        {
            id = Guid.NewGuid().ToString(),
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = request.Model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = content
                    },
                    finish_reason = "stop"
                }
            },
            usage = new
            {
                prompt_tokens = EstimateTokens(request.GetLastMessageContent()),
                completion_tokens = EstimateTokens(content),
                total_tokens = EstimateTokens(request.GetLastMessageContent()) + EstimateTokens(content)
            }
        };
    }

    /// <summary>
    /// Create error response
    /// </summary>
    private object CreateErrorResponse(Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request, string message)
    {
        return new
        {
            id = Guid.NewGuid().ToString(),
            @object = "error",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = request.Model,
            error = new
            {
                message = message,
                type = "execution_error",
                code = "agent_execution_failed"
            }
        };
    }

    /// <summary>
    /// Estimate token count (rough approximation)
    /// </summary>
    private int EstimateTokens(string text)
    {
        return (text?.Length ?? 0) / 4; // Rough estimate: 4 chars per token
    }
}