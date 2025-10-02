using Microsoft.Agents.AI.DevUI.Models.Discovery;

namespace Microsoft.Agents.AI.DevUI.Core;

/// <summary>
/// Discovers and manages Agent Framework entities (agents and workflows)
/// </summary>
public interface IEntityDiscovery
{
    /// <summary>
    /// Discover entities from a directory
    /// </summary>
    Task<List<EntityInfo>> DiscoverEntitiesFromDirectoryAsync(string entitiesDir);

    /// <summary>
    /// Register an in-memory entity
    /// </summary>
    void RegisterInMemoryEntity(object entity);

    /// <summary>
    /// List all registered entities
    /// </summary>
    List<EntityInfo> ListEntities();

    /// <summary>
    /// Get entity information by ID
    /// </summary>
    EntityInfo? GetEntityInfo(string entityId);

    /// <summary>
    /// Get the actual entity object by ID
    /// </summary>
    object? GetEntityObject(string entityId);

    /// <summary>
    /// Remove an entity by ID
    /// </summary>
    bool RemoveEntity(string entityId);

    /// <summary>
    /// Clear all entities
    /// </summary>
    void ClearEntities();
}