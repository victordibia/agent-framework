using Microsoft.Agents.AI.DevUI.Models;

namespace Microsoft.Agents.AI.DevUI.Services;

public class ThreadService
{
    private readonly Dictionary<string, DevUIThread> _threads = new();
    private readonly Dictionary<string, List<DevUIMessage>> _threadMessages = new();
    private readonly ILogger<ThreadService> _logger;

    public ThreadService(ILogger<ThreadService> logger)
    {
        _logger = logger;
    }

    public DevUIThread CreateThread(string agentId)
    {
        var thread = new DevUIThread
        {
            Id = GenerateThreadId(),
            AgentId = agentId,
            Object = "thread",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Metadata = new Dictionary<string, object>
            {
                ["agent_id"] = agentId,
                ["created_by"] = "devui"
            }
        };

        _threads[thread.Id] = thread;
        _threadMessages[thread.Id] = new List<DevUIMessage>();

        _logger.LogInformation("Created thread {ThreadId} for agent {AgentId}", thread.Id, agentId);
        return thread;
    }

    public List<DevUIThread> ListThreads(string? agentId = null)
    {
        var threads = _threads.Values.ToList();

        if (!string.IsNullOrEmpty(agentId))
        {
            threads = threads.Where(t => t.AgentId == agentId).ToList();
        }

        return threads.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public DevUIThread? GetThread(string threadId)
    {
        return _threads.TryGetValue(threadId, out var thread) ? thread : null;
    }

    public bool DeleteThread(string threadId)
    {
        var removed = _threads.Remove(threadId);
        if (removed)
        {
            _threadMessages.Remove(threadId);
            _logger.LogInformation("Deleted thread {ThreadId}", threadId);
        }
        return removed;
    }

    public List<DevUIMessage>? GetThreadMessages(string threadId)
    {
        return _threadMessages.TryGetValue(threadId, out var messages) ? messages : null;
    }

    public void AddMessageToThread(string threadId, DevUIMessage message)
    {
        if (_threadMessages.TryGetValue(threadId, out var messages))
        {
            messages.Add(message);
            _logger.LogInformation("Added message to thread {ThreadId}", threadId);
        }
    }

    private static string GenerateThreadId()
    {
        return $"thread_{Guid.NewGuid().ToString("N")[..16]}";
    }
}