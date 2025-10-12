using Microsoft.Agents.AI.DevUI.Models;
using Microsoft.Agents.AI;
using System.Collections.Concurrent;

namespace Microsoft.Agents.AI.DevUI.Services;

/// <summary>
/// Manages conversations using OpenAI Conversations API format
/// Wraps AgentFramework's AgentThread underneath for compatibility
/// </summary>
public class ConversationService
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();
    private readonly ConcurrentDictionary<string, List<ConversationItem>> _items = new();
    private readonly ConcurrentDictionary<string, AgentThread> _threads = new();
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(ILogger<ConversationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create a new conversation
    /// AgentThread will be created lazily when first needed by GetOrCreateThread()
    /// </summary>
    public Conversation CreateConversation(Dictionary<string, string>? metadata = null)
    {
        var conversationId = $"conv_{Guid.NewGuid():N}";
        var conversation = new Conversation
        {
            Id = conversationId,
            Object = "conversation",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Metadata = metadata ?? new Dictionary<string, string>()
        };

        _conversations[conversationId] = conversation;
        _items[conversationId] = new List<ConversationItem>();

        _logger.LogInformation("Created conversation {ConversationId}", conversationId);
        return conversation;
    }

    /// <summary>
    /// Get conversation by ID
    /// </summary>
    public Conversation? GetConversation(string conversationId)
    {
        return _conversations.TryGetValue(conversationId, out var conversation)
            ? conversation
            : null;
    }

    /// <summary>
    /// Get underlying AgentThread for a conversation
    /// This is critical for multi-turn conversations with function calling
    /// </summary>
    public AgentThread? GetThread(string conversationId)
    {
        return _threads.TryGetValue(conversationId, out var thread) ? thread : null;
    }

    /// <summary>
    /// Get or create an AgentThread for a conversation using the provided agent
    /// Threads are created lazily because they require an agent instance
    /// </summary>
    public AgentThread GetOrCreateThread(string conversationId, AIAgent agent)
    {
        if (!_conversations.ContainsKey(conversationId))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found");
        }

        // Get existing thread or create new one
        if (!_threads.TryGetValue(conversationId, out var thread))
        {
            thread = agent.GetNewThread();
            _threads[conversationId] = thread;
            _logger.LogInformation("Created new AgentThread for conversation {ConversationId}", conversationId);
        }

        return thread;
    }

    /// <summary>
    /// Update conversation metadata
    /// </summary>
    public Conversation UpdateConversation(string conversationId, Dictionary<string, string> metadata)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found");
        }

        conversation.Metadata = metadata;
        _conversations[conversationId] = conversation;

        _logger.LogInformation("Updated conversation {ConversationId}", conversationId);
        return conversation;
    }

    /// <summary>
    /// Delete a conversation and its AgentThread
    /// </summary>
    public ConversationDeletedResource DeleteConversation(string conversationId)
    {
        if (!_conversations.TryRemove(conversationId, out _))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found");
        }

        _items.TryRemove(conversationId, out _);
        _threads.TryRemove(conversationId, out _);

        _logger.LogInformation("Deleted conversation {ConversationId} and its AgentThread", conversationId);

        return new ConversationDeletedResource
        {
            Id = conversationId,
            Object = "conversation.deleted",
            Deleted = true
        };
    }

    /// <summary>
    /// List all conversations, optionally filtered by agent_id in metadata
    /// </summary>
    public ConversationListResponse ListConversations(string? agentId = null, int limit = 100)
    {
        var conversations = _conversations.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(agentId))
        {
            conversations = conversations.Where(c =>
                c.Metadata.TryGetValue("agent_id", out var id) && id == agentId);
        }

        var conversationsList = conversations
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToList();

        return new ConversationListResponse
        {
            Object = "list",
            Data = conversationsList,
            HasMore = false,
            FirstId = conversationsList.FirstOrDefault()?.Id,
            LastId = conversationsList.LastOrDefault()?.Id
        };
    }

    /// <summary>
    /// Add items (messages) to a conversation
    /// </summary>
    public List<ConversationItem> AddItems(string conversationId, List<Dictionary<string, object>> itemDicts)
    {
        if (!_conversations.ContainsKey(conversationId))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found");
        }

        if (!_items.TryGetValue(conversationId, out var items))
        {
            items = new List<ConversationItem>();
            _items[conversationId] = items;
        }

        var addedItems = new List<ConversationItem>();

        foreach (var itemDict in itemDicts)
        {
            var item = new ConversationItem
            {
                Id = $"item_{Guid.NewGuid():N}",
                Object = "conversation.item",
                Type = itemDict.TryGetValue("type", out var type) ? type.ToString() ?? "message" : "message",
                Role = itemDict.TryGetValue("role", out var role) ? role.ToString() ?? "user" : "user",
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // Parse content
            if (itemDict.TryGetValue("content", out var contentObj) && contentObj is List<object> contentList)
            {
                foreach (var contentItem in contentList)
                {
                    if (contentItem is Dictionary<string, object> contentDict)
                    {
                        var content = new ConversationContent
                        {
                            Type = contentDict.TryGetValue("type", out var contentType)
                                ? contentType.ToString() ?? "text"
                                : "text",
                            Text = contentDict.TryGetValue("text", out var text)
                                ? text.ToString()
                                : null
                        };
                        item.Content.Add(content);
                    }
                }
            }

            items.Add(item);
            addedItems.Add(item);
        }

        _logger.LogInformation("Added {Count} items to conversation {ConversationId}", addedItems.Count, conversationId);
        return addedItems;
    }

    /// <summary>
    /// Get items from a conversation
    /// Note: .NET AgentThread doesn't expose message history like Python does,
    /// so we cache items manually as they're added
    /// </summary>
    public ConversationItemListResponse GetItems(string conversationId, int limit = 100, string? after = null, string order = "desc")
    {
        if (!_items.TryGetValue(conversationId, out var items))
        {
            // Initialize empty list if conversation exists but has no items yet
            if (_conversations.ContainsKey(conversationId))
            {
                items = new List<ConversationItem>();
                _items[conversationId] = items;
            }
            else
            {
                return new ConversationItemListResponse
                {
                    Object = "list",
                    Data = new List<ConversationItem>()
                };
            }
        }

        var itemsList = items.AsEnumerable();

        // Filter by 'after' cursor
        if (!string.IsNullOrEmpty(after))
        {
            var afterIndex = items.FindIndex(i => i.Id == after);
            if (afterIndex >= 0)
            {
                itemsList = items.Skip(afterIndex + 1);
            }
        }

        // Order
        if (order == "asc")
        {
            itemsList = itemsList.OrderBy(i => i.CreatedAt);
        }
        else
        {
            itemsList = itemsList.OrderByDescending(i => i.CreatedAt);
        }

        var resultItems = itemsList.Take(limit).ToList();

        return new ConversationItemListResponse
        {
            Object = "list",
            Data = resultItems,
            HasMore = resultItems.Count >= limit,
            FirstId = resultItems.FirstOrDefault()?.Id,
            LastId = resultItems.LastOrDefault()?.Id
        };
    }

    /// <summary>
    /// Get a specific item from a conversation
    /// </summary>
    public ConversationItem? GetItem(string conversationId, string itemId)
    {
        if (!_items.TryGetValue(conversationId, out var items))
        {
            return null;
        }

        return items.FirstOrDefault(i => i.Id == itemId);
    }

    /// <summary>
    /// Add a simple text message to a conversation
    /// Used internally by ExecutionService to populate conversation history
    /// </summary>
    public void AddMessage(string conversationId, string role, string text)
    {
        if (!_conversations.ContainsKey(conversationId))
        {
            return;
        }

        if (!_items.TryGetValue(conversationId, out var items))
        {
            items = new List<ConversationItem>();
            _items[conversationId] = items;
        }

        var item = new ConversationItem
        {
            Id = $"item_{Guid.NewGuid():N}",
            Object = "conversation.item",
            Type = "message",
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Content = new List<ConversationContent>
            {
                new ConversationContent { Type = "text", Text = text }
            }
        };

        items.Add(item);
    }
}
