using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.DevUI.Models;
using Microsoft.Agents.DevUI.Services;
using System.Text.Json;
using System.Text;

namespace Microsoft.Agents.DevUI.Controllers;

[ApiController]
[Route("/")]
public class DevUIController : ControllerBase
{
    private readonly EntityDiscoveryService _discoveryService;
    private readonly ExecutionService _executionService;
    private readonly ThreadService _threadService;
    private readonly ILogger<DevUIController> _logger;

    public DevUIController(
        EntityDiscoveryService discoveryService,
        ExecutionService executionService,
        ThreadService threadService,
        ILogger<DevUIController> logger)
    {
        _discoveryService = discoveryService;
        _executionService = executionService;
        _threadService = threadService;
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
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
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

        // Return in the expected wrapper format to match Python backend
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
            return NotFound(new { error = $"Entity '{entityId}' not found" });
        }

        _logger.LogInformation("Returning info for entity: {EntityId}", entityId);
        return Ok(entityInfo);
    }

    /// <summary>
    /// Execute entity with OpenAI-compatible API
    /// </summary>
    [HttpPost("v1/responses")]
    public async Task<IActionResult> ExecuteEntity([FromBody] DevUIExecutionRequest request)
    {
        try
        {
            _logger.LogInformation("Executing entity with model: {Model}, streaming: {Stream}",
                request.Model, request.Stream);

            // Extract entity_id from extra_body
            string? entityId = request.GetEntityId();

            if (string.IsNullOrEmpty(entityId))
            {
                return BadRequest(new { error = "entity_id is required in extra_body" });
            }

            // Get the entity
            var entityInfo = _discoveryService.GetEntityInfo(entityId);
            if (entityInfo == null)
            {
                return NotFound(new { error = $"Entity '{entityId}' not found" });
            }

            // Handle streaming vs non-streaming
            if (request.Stream)
            {
                // Return Server-Sent Events for streaming
                Response.Headers.Append("Content-Type", "text/event-stream");
                Response.Headers.Append("Cache-Control", "no-cache");
                Response.Headers.Append("Connection", "keep-alive");
                Response.Headers.Append("Access-Control-Allow-Origin", "*");

                return new StreamingActionResult(_executionService, entityId, request, HttpContext.RequestAborted);
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
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Streaming methods temporarily removed - will be added back once async generator issues are fixed

    /// <summary>
    /// Create a new thread for an agent
    /// </summary>
    [HttpPost("v1/threads")]
    public IActionResult CreateThread([FromBody] CreateThreadRequest request)
    {
        try
        {
            var thread = _threadService.CreateThread(request.AgentId);
            _logger.LogInformation("Created thread {ThreadId} for agent {AgentId}", thread.Id, request.AgentId);
            return Ok(thread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating thread for agent {AgentId}", request.AgentId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// List threads for an agent
    /// </summary>
    [HttpGet("v1/threads")]
    public IActionResult ListThreads([FromQuery] string? agentId = null)
    {
        try
        {
            var threads = _threadService.ListThreads(agentId);
            _logger.LogInformation("Returning {Count} threads for agent {AgentId}", threads.Count, agentId ?? "all");

            // Return in the expected wrapper format to match Python backend
            var response = new { @object = "list", data = threads };
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing threads for agent {AgentId}", agentId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific thread
    /// </summary>
    [HttpGet("v1/threads/{threadId}")]
    public IActionResult GetThread(string threadId)
    {
        try
        {
            var thread = _threadService.GetThread(threadId);
            if (thread == null)
            {
                return NotFound(new { error = $"Thread '{threadId}' not found" });
            }

            _logger.LogInformation("Returning thread {ThreadId}", threadId);
            return Ok(thread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting thread {ThreadId}", threadId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a thread
    /// </summary>
    [HttpDelete("v1/threads/{threadId}")]
    public IActionResult DeleteThread(string threadId)
    {
        try
        {
            var success = _threadService.DeleteThread(threadId);
            if (!success)
            {
                return NotFound(new { error = $"Thread '{threadId}' not found" });
            }

            _logger.LogInformation("Deleted thread {ThreadId}", threadId);
            return Ok(new { deleted = true, id = threadId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting thread {ThreadId}", threadId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get messages from a thread
    /// </summary>
    [HttpGet("v1/threads/{threadId}/messages")]
    public IActionResult GetThreadMessages(string threadId)
    {
        try
        {
            var messages = _threadService.GetThreadMessages(threadId);
            if (messages == null)
            {
                return NotFound(new { error = $"Thread '{threadId}' not found" });
            }

            _logger.LogInformation("Returning {Count} messages for thread {ThreadId}", messages.Count, threadId);

            // Return in the expected wrapper format to match Python backend
            var response = new { @object = "list", data = messages, thread_id = threadId };
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for thread {ThreadId}", threadId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class CreateThreadRequest
{
    public string AgentId { get; set; } = "";
}

/// <summary>
/// Custom ActionResult for Server-Sent Events streaming
/// </summary>
public class StreamingActionResult : IActionResult
{
    private readonly ExecutionService _executionService;
    private readonly string _entityId;
    private readonly DevUIExecutionRequest _request;
    private readonly CancellationToken _cancellationToken;

    public StreamingActionResult(ExecutionService executionService, string entityId, DevUIExecutionRequest request, CancellationToken cancellationToken)
    {
        _executionService = executionService;
        _entityId = entityId;
        _request = request;
        _cancellationToken = cancellationToken;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;

        try
        {
            await foreach (var streamEvent in _executionService.ExecuteEntityStreamingAsync(_entityId, _request, _cancellationToken))
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
                id = Guid.NewGuid().ToString(),
                @object = "error",
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