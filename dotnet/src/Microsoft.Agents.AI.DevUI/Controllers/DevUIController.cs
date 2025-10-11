using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI.DevUI.Models;
using Microsoft.Agents.AI.DevUI.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using System.Text.Json;
using System.Text;

namespace Microsoft.Agents.AI.DevUI.Controllers;

[ApiController]
[Route("/")]
public class DevUIController : ControllerBase
{
    private readonly EntityDiscoveryService _discoveryService;
    private readonly ExecutionService _executionService;
    private readonly ConversationService _conversationService;
    private readonly ILogger<DevUIController> _logger;

    public DevUIController(
        EntityDiscoveryService discoveryService,
        ExecutionService executionService,
        ConversationService conversationService,
        ILogger<DevUIController> logger)
    {
        _discoveryService = discoveryService;
        _executionService = executionService;
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        var entities = _discoveryService.ListEntities();
        return Ok(new
        {
            status = "healthy",
            entities_count = entities.Count,
            framework = "agent-framework",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// List all discovered entities
    /// </summary>
    [HttpGet("v1/entities")]
    public IActionResult ListEntities()
    {
        var entities = _discoveryService.ListEntities();
        _logger.LogInformation("Returning {Count} entities", entities.Count);

        var response = new DiscoveryResponse
        {
            Entities = entities
        };
        return Ok(response);
    }

    /// <summary>
    /// Get detailed information about a specific entity
    /// </summary>
    [HttpGet("v1/entities/{entityId}/info")]
    public IActionResult GetEntityInfo(string entityId)
    {
        var entityInfo = _discoveryService.GetEntityInfo(entityId);
        if (entityInfo == null)
        {
            _logger.LogWarning("Entity not found: {EntityId}", entityId);
            return NotFound(new { error = new { message = $"Entity '{entityId}' not found", type = "entity_not_found" } });
        }

        // For workflows, populate workflow_dump for visualization
        if (entityInfo.Type == "workflow")
        {
            var entityObject = _discoveryService.GetEntityObject(entityId);
            if (entityObject is Workflow workflow)
            {
                try
                {
                    // Use the ToDevUIDict extension method to get Python-compatible format
                    entityInfo.WorkflowDump = workflow.ToDevUIDict();
                    _logger.LogDebug("Successfully serialized workflow_dump for {EntityId}", entityId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to serialize workflow_dump for {EntityId}", entityId);
                }
            }
        }

        _logger.LogInformation("Returning info for entity: {EntityId}", entityId);
        return Ok(entityInfo);
    }

    /// <summary>
    /// Execute entity with OpenAI Responses API
    /// Uses 'model' field as entity_id (NOT extra_body.entity_id)
    /// </summary>
    [HttpPost("v1/responses")]
    public async Task<IActionResult> ExecuteEntity([FromBody] Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request)
    {
        try
        {
            // Get entity_id from model field (OpenAI standard!)
            string entityId = request.GetEntityId();
            _logger.LogInformation("Executing entity '{EntityId}' (from model field), streaming: {Stream}",
                entityId, request.Stream);

            // Get the entity
            var entityInfo = _discoveryService.GetEntityInfo(entityId);
            if (entityInfo == null)
            {
                return NotFound(new { error = new { message = $"Entity '{entityId}' not found", type = "entity_not_found" } });
            }

            // Handle streaming vs non-streaming
            if (request.Stream)
            {
                // Return Server-Sent Events for streaming
                Response.Headers.Append("Content-Type", "text/event-stream");
                Response.Headers.Append("Cache-Control", "no-cache");
                Response.Headers.Append("Connection", "keep-alive");
                Response.Headers.Append("Access-Control-Allow-Origin", "*");

                return new StreamingActionResult(_executionService, _conversationService, entityId, request, HttpContext.RequestAborted);
            }
            else
            {
                // Execute non-streaming
                var result = await _executionService.ExecuteEntityAsync(entityId, request);
                return Ok(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing entity");
            return StatusCode(500, new { error = new { message = ex.Message, type = "execution_error" } });
        }
    }

    #region Conversations API

    /// <summary>
    /// Create a new conversation (OpenAI Conversations API)
    /// </summary>
    [HttpPost("v1/conversations")]
    public IActionResult CreateConversation([FromBody] CreateConversationRequest? request)
    {
        try
        {
            var conversation = _conversationService.CreateConversation(request?.Metadata);
            _logger.LogInformation("Created conversation {ConversationId}", conversation.Id);
            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation");
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// List conversations, optionally filtered by agent_id
    /// </summary>
    [HttpGet("v1/conversations")]
    public IActionResult ListConversations([FromQuery] string? agent_id = null, [FromQuery] int limit = 100)
    {
        try
        {
            var response = _conversationService.ListConversations(agent_id, limit);
            _logger.LogInformation("Returning {Count} conversations", response.Data.Count);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing conversations");
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// Get a specific conversation
    /// </summary>
    [HttpGet("v1/conversations/{conversationId}")]
    public IActionResult GetConversation(string conversationId)
    {
        try
        {
            var conversation = _conversationService.GetConversation(conversationId);
            if (conversation == null)
            {
                return NotFound(new { error = new { message = $"Conversation '{conversationId}' not found", type = "conversation_not_found" } });
            }

            _logger.LogInformation("Returning conversation {ConversationId}", conversationId);
            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// Update conversation metadata
    /// </summary>
    [HttpPost("v1/conversations/{conversationId}")]
    public IActionResult UpdateConversation(string conversationId, [FromBody] UpdateConversationRequest request)
    {
        try
        {
            var conversation = _conversationService.UpdateConversation(conversationId, request.Metadata);
            _logger.LogInformation("Updated conversation {ConversationId}", conversationId);
            return Ok(conversation);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = new { message = $"Conversation '{conversationId}' not found", type = "conversation_not_found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// Delete a conversation
    /// </summary>
    [HttpDelete("v1/conversations/{conversationId}")]
    public IActionResult DeleteConversation(string conversationId)
    {
        try
        {
            var result = _conversationService.DeleteConversation(conversationId);
            _logger.LogInformation("Deleted conversation {ConversationId}", conversationId);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = new { message = $"Conversation '{conversationId}' not found", type = "conversation_not_found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// Add items to a conversation
    /// </summary>
    [HttpPost("v1/conversations/{conversationId}/items")]
    public IActionResult AddConversationItems(string conversationId, [FromBody] AddConversationItemsRequest request)
    {
        try
        {
            var items = _conversationService.AddItems(conversationId, request.Items);
            _logger.LogInformation("Added {Count} items to conversation {ConversationId}", items.Count, conversationId);
            return Ok(new { @object = "list", data = items });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = new { message = $"Conversation '{conversationId}' not found", type = "conversation_not_found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding items to conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// Get items from a conversation
    /// </summary>
    [HttpGet("v1/conversations/{conversationId}/items")]
    public IActionResult GetConversationItems(
        string conversationId,
        [FromQuery] int limit = 100,
        [FromQuery] string? after = null,
        [FromQuery] string order = "desc")
    {
        try
        {
            var response = _conversationService.GetItems(conversationId, limit, after, order);
            _logger.LogInformation("Returning {Count} items from conversation {ConversationId}", response.Data.Count, conversationId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting items from conversation {ConversationId}", conversationId);
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    /// <summary>
    /// Get a specific item from a conversation
    /// </summary>
    [HttpGet("v1/conversations/{conversationId}/items/{itemId}")]
    public IActionResult GetConversationItem(string conversationId, string itemId)
    {
        try
        {
            var item = _conversationService.GetItem(conversationId, itemId);
            if (item == null)
            {
                return NotFound(new { error = new { message = $"Item '{itemId}' not found in conversation '{conversationId}'", type = "item_not_found" } });
            }

            _logger.LogInformation("Returning item {ItemId} from conversation {ConversationId}", itemId, conversationId);
            return Ok(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting item {ItemId} from conversation {ConversationId}", itemId, conversationId);
            return StatusCode(500, new { error = new { message = ex.Message, type = "conversation_error" } });
        }
    }

    #endregion
}

/// <summary>
/// Custom ActionResult for Server-Sent Events streaming
/// </summary>
public class StreamingActionResult : IActionResult
{
    private readonly ExecutionService _executionService;
    private readonly ConversationService _conversationService;
    private readonly string _entityId;
    private readonly Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest _request;
    private readonly CancellationToken _cancellationToken;

    public StreamingActionResult(
        ExecutionService executionService,
        ConversationService conversationService,
        string entityId,
        Microsoft.Agents.AI.DevUI.Models.Execution.DevUIExecutionRequest request,
        CancellationToken cancellationToken)
    {
        _executionService = executionService;
        _conversationService = conversationService;
        _entityId = entityId;
        _request = request;
        _cancellationToken = cancellationToken;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;

        try
        {
            // Get conversation/thread if provided
            var conversationId = _request.GetConversationId();
            AgentThread? thread = null;
            if (!string.IsNullOrEmpty(conversationId))
            {
                thread = _conversationService.GetThread(conversationId);
            }

            await foreach (var streamEvent in _executionService.ExecuteEntityStreamingAsync(_entityId, _request, thread, _cancellationToken))
            {
                if (_cancellationToken.IsCancellationRequested)
                    break;

                var json = JsonSerializer.Serialize(streamEvent);
                var eventData = $"data: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(eventData);

                await response.Body.WriteAsync(bytes, _cancellationToken);
                await response.Body.FlushAsync(_cancellationToken);
            }

            // Send final [DONE] signal
            var doneBytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
            await response.Body.WriteAsync(doneBytes, _cancellationToken);
            await response.Body.FlushAsync(_cancellationToken);
        }
        catch (Exception ex)
        {
            // Send error event
            var errorEvent = new
            {
                type = "error",
                error = new
                {
                    message = ex.Message,
                    type = "execution_error"
                }
            };

            var errorJson = JsonSerializer.Serialize(errorEvent);
            var errorData = $"data: {errorJson}\n\n";
            var errorBytes = Encoding.UTF8.GetBytes(errorData);

            await response.Body.WriteAsync(errorBytes, _cancellationToken);
            await response.Body.FlushAsync(_cancellationToken);
        }
    }
}
