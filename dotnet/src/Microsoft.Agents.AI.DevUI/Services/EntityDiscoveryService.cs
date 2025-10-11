using Microsoft.Agents.AI.DevUI.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using System.Reflection;

namespace Microsoft.Agents.AI.DevUI.Services;

public class EntityDiscoveryService
{
    private readonly Dictionary<string, EntityInfo> _entityInfos = new();
    private readonly Dictionary<string, object> _entityObjects = new();
    private readonly ILogger<EntityDiscoveryService> _logger;

    public EntityDiscoveryService(ILogger<EntityDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<List<EntityInfo>> DiscoverEntitiesFromDirectoryAsync(string? entitiesDir)
    {
        var entities = new List<EntityInfo>();

        if (string.IsNullOrEmpty(entitiesDir))
        {
            _logger.LogWarning("Entities directory not specified");
            return entities;
        }

        // Resolve the path - try both absolute and relative to current directory
        var resolvedPath = Path.GetFullPath(entitiesDir);
        _logger.LogDebug("Attempting to resolve path: {OriginalPath} -> {ResolvedPath}", entitiesDir, resolvedPath);

        // If still not found, try relative to the application base directory
        if (!Directory.Exists(resolvedPath))
        {
            var appBasePath = AppDomain.CurrentDomain.BaseDirectory;
            var alternativePath = Path.Combine(appBasePath, entitiesDir);
            _logger.LogDebug("Directory not found at {ResolvedPath}, trying {AlternativePath}", resolvedPath, alternativePath);

            if (Directory.Exists(alternativePath))
            {
                resolvedPath = alternativePath;
            }
        }

        // Also check the current working directory
        if (!Directory.Exists(resolvedPath))
        {
            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), entitiesDir);
            _logger.LogDebug("Still not found, trying relative to CWD: {CwdPath}", cwdPath);

            if (Directory.Exists(cwdPath))
            {
                resolvedPath = cwdPath;
            }
        }

        if (!Directory.Exists(resolvedPath))
        {
            _logger.LogWarning("Entities directory not found: {Dir} (tried: {ResolvedPath}, CWD: {Cwd})",
                entitiesDir, resolvedPath, Directory.GetCurrentDirectory());
            return entities;
        }

        _logger.LogInformation("Discovering entities from directory: {Dir} (resolved: {ResolvedPath})", entitiesDir, resolvedPath);

        // Scan only one level deep (matching Python's behavior)
        // This prevents discovering unintended files in nested subdirectories

        // 1. Scan for .cs files in the top-level directory
        var csFiles = Directory.GetFiles(resolvedPath, "*.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in csFiles)
        {
            try
            {
                // This is a simplified discovery - in a real implementation,
                // you might want to compile and load the assemblies dynamically
                var content = await File.ReadAllTextAsync(file);

                // Look for agent patterns
                if (content.Contains(": AIAgent") || (content.Contains("public class") && content.Contains("Agent")))
                {
                    var entityId = $"agent_{Path.GetFileNameWithoutExtension(file).ToLowerInvariant()}";
                    var entityInfo = new EntityInfo
                    {
                        Id = entityId,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Type = "agent",
                        Description = $"Agent from {Path.GetFileName(file)}",
                        Framework = "agent-framework",
                        Tools = new List<object>(),
                        Metadata = new Dictionary<string, object>
                        {
                            { "module_path", file },
                            { "source", "directory" }
                        }
                    };

                    entities.Add(entityInfo);
                    _entityInfos[entityId] = entityInfo;
                    _logger.LogInformation("Discovered agent: {Id}", entityId);
                }

                // Look for workflow patterns (including generic workflows like Workflow<string>)
                if (content.Contains(": Workflow<") || content.Contains(": Workflow") ||
                    (content.Contains("public class") && content.Contains("Workflow")))
                {
                    var entityId = $"workflow_{Path.GetFileNameWithoutExtension(file).ToLowerInvariant()}";
                    var entityInfo = new EntityInfo
                    {
                        Id = entityId,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Type = "workflow",
                        Description = $"Workflow from {Path.GetFileName(file)}",
                        Framework = "agent-framework",
                        Tools = new List<object>(),
                        Metadata = new Dictionary<string, object>
                        {
                            { "module_path", file },
                            { "source", "directory" }
                        },
                        Executors = new List<string>(),
                        InputSchema = new Dictionary<string, object> { { "type", "string" } },
                        InputTypeName = "String"
                    };

                    entities.Add(entityInfo);
                    _entityInfos[entityId] = entityInfo;
                    _logger.LogInformation("Discovered workflow: {Id}", entityId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file: {File}", file);
            }
        }

        // 2. Scan for subdirectories (folder-based entities, matching Python's structure)
        var subDirectories = Directory.GetDirectories(resolvedPath);

        foreach (var dir in subDirectories)
        {
            var dirName = Path.GetFileName(dir);

            // Skip hidden directories and common build/config folders
            if (dirName.StartsWith('.') || dirName == "__pycache__" || dirName == "bin" || dirName == "obj")
            {
                continue;
            }

            try
            {
                // Look for .cs files in the subdirectory (one level only)
                var dirCsFiles = Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly);

                foreach (var file in dirCsFiles)
                {
                    var content = await File.ReadAllTextAsync(file);

                    // Look for agent patterns
                    if (content.Contains(": AIAgent") || (content.Contains("public class") && content.Contains("Agent")))
                    {
                        var entityId = $"agent_{dirName.ToLowerInvariant()}";
                        var entityInfo = new EntityInfo
                        {
                            Id = entityId,
                            Name = Path.GetFileNameWithoutExtension(file),
                            Type = "agent",
                            Description = $"Agent from {dirName}/{Path.GetFileName(file)}",
                            Framework = "agent-framework",
                            Tools = new List<object>(),
                            Metadata = new Dictionary<string, object>
                            {
                                { "module_path", file },
                                { "source", "directory" },
                                { "folder_name", dirName }
                            }
                        };

                        entities.Add(entityInfo);
                        _entityInfos[entityId] = entityInfo;
                        _logger.LogInformation("Discovered agent in folder: {Id} from {Folder}", entityId, dirName);
                    }

                    // Look for workflow patterns
                    if (content.Contains(": Workflow<") || content.Contains(": Workflow") ||
                        (content.Contains("public class") && content.Contains("Workflow")))
                    {
                        var entityId = $"workflow_{dirName.ToLowerInvariant()}";
                        var entityInfo = new EntityInfo
                        {
                            Id = entityId,
                            Name = Path.GetFileNameWithoutExtension(file),
                            Type = "workflow",
                            Description = $"Workflow from {dirName}/{Path.GetFileName(file)}",
                            Framework = "agent-framework",
                            Tools = new List<object>(),
                            Metadata = new Dictionary<string, object>
                            {
                                { "module_path", file },
                                { "source", "directory" },
                                { "folder_name", dirName }
                            },
                            Executors = new List<string>(),
                            InputSchema = new Dictionary<string, object> { { "type", "string" } },
                            InputTypeName = "String"
                        };

                        entities.Add(entityInfo);
                        _entityInfos[entityId] = entityInfo;
                        _logger.LogInformation("Discovered workflow in folder: {Id} from {Folder}", entityId, dirName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process directory: {Dir}", dir);
            }
        }

        _logger.LogInformation("Discovered {Count} entities total", entities.Count);
        return entities;
    }

    public void RegisterInMemoryEntity(object entity)
    {
        var entityInfo = CreateEntityInfoFromObject(entity);
        _entityInfos[entityInfo.Id] = entityInfo;
        _entityObjects[entityInfo.Id] = entity;
        _logger.LogInformation("Registered in-memory entity: {Id}", entityInfo.Id);
    }

    private EntityInfo CreateEntityInfoFromObject(object entity)
    {
        var type = entity.GetType();
        var entityId = $"{type.Name.ToLowerInvariant()}_{Guid.NewGuid().ToString("N")[..8]}";

        var entityInfo = new EntityInfo
        {
            Id = entityId,
            Name = type.Name,
            Type = DetermineEntityType(entity),
            Description = $"In-memory {type.Name}",
            Framework = "agent-framework",
            Tools = new List<object>(),
            Metadata = new Dictionary<string, object>
            {
                { "source", "in-memory" },
                { "type_name", type.FullName ?? type.Name }
            }
        };

        // Additional setup for specific types
        if (entity is AIAgent agent)
        {
            entityInfo.Name = agent.Name ?? agent.Id;
            entityInfo.Description = agent.Description ?? entityInfo.Description;

            // Add agent-specific metadata
            entityInfo.Metadata["agent_id"] = agent.Id;
            if (!string.IsNullOrEmpty(agent.Name))
                entityInfo.Metadata["agent_name"] = agent.Name;
        }
        else if (entityInfo.Type == "workflow")
        {
            // Add workflow-specific fields
            entityInfo.Executors = new List<string>();
            entityInfo.InputSchema = new Dictionary<string, object> { { "type", "string" } };
            entityInfo.InputTypeName = "String";
        }

        return entityInfo;
    }

    private static string DetermineEntityType(object entity)
    {
        return entity switch
        {
            AIAgent => "agent",
            Workflow => "workflow",
            _ => "unknown"
        };
    }

    public List<EntityInfo> ListEntities()
    {
        return _entityInfos.Values.ToList();
    }

    public EntityInfo? GetEntityInfo(string entityId)
    {
        return _entityInfos.TryGetValue(entityId, out var info) ? info : null;
    }

    public object? GetEntityObject(string entityId)
    {
        return _entityObjects.TryGetValue(entityId, out var obj) ? obj : null;
    }
}