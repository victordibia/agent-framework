using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Agents.AI.DevUI.Services;

/// <summary>
/// Hosted service that discovers and registers agents/workflows from the DI container
/// Runs at application startup to populate the EntityDiscoveryService
/// </summary>
public class DevUIHostedService : IHostedService
{
    private readonly EntityDiscoveryService _discoveryService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DevUIHostedService> _logger;
    private readonly DevUIOptions _options;

    public DevUIHostedService(
        EntityDiscoveryService discoveryService,
        IServiceProvider serviceProvider,
        IOptions<DevUIOptions> options,
        ILogger<DevUIHostedService> logger)
    {
        _discoveryService = discoveryService;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 DevUI Hosted Service starting...");

        // Discover entities from DI container if enabled
        if (_options.DiscoverFromDI)
        {
            DiscoverEntitiesFromDI();
        }

        // Discover entities from file system if directory specified
        if (!string.IsNullOrEmpty(_options.EntitiesDir))
        {
            // Note: File discovery is not yet implemented in EntityDiscoveryService
            _logger.LogWarning("File discovery from directory not yet implemented: {Dir}", _options.EntitiesDir);
        }

        var entityInfos = _discoveryService.GetType()
            .GetField("_entityInfos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .GetValue(_discoveryService) as System.Collections.IDictionary;
        var entityCount = entityInfos?.Count ?? 0;
        _logger.LogInformation("✅ DevUI discovered {EntityCount} entities", entityCount);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 DevUI Hosted Service stopping...");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Discovers agents and workflows registered in the DI container
    /// </summary>
    private void DiscoverEntitiesFromDI()
    {
        var discoveredCount = 0;

        try
        {
            // Discover all AIAgent registrations
            var agents = _serviceProvider.GetService<IEnumerable<AIAgent>>();
            if (agents != null)
            {
                foreach (var agent in agents)
                {
                    _discoveryService.RegisterInMemoryEntity(agent);
                    discoveredCount++;
                    _logger.LogDebug("Registered agent from DI: {AgentName}", agent.Name ?? agent.Id);
                }
            }

            // Discover single AIAgent registration
            var singleAgent = _serviceProvider.GetService<AIAgent>();
            if (singleAgent != null)
            {
                _discoveryService.RegisterInMemoryEntity(singleAgent);
                discoveredCount++;
                _logger.LogDebug("Registered agent from DI: {AgentName}", singleAgent.Name ?? singleAgent.Id);
            }

            // Discover all Workflow registrations
            var workflows = _serviceProvider.GetService<IEnumerable<Workflow>>();
            if (workflows != null)
            {
                foreach (var workflow in workflows)
                {
                    _discoveryService.RegisterInMemoryEntity(workflow);
                    discoveredCount++;
                    _logger.LogDebug("Registered workflow from DI: {WorkflowName}", workflow.Name ?? workflow.GetType().Name);
                }
            }

            // Discover single Workflow registration
            var singleWorkflow = _serviceProvider.GetService<Workflow>();
            if (singleWorkflow != null)
            {
                _discoveryService.RegisterInMemoryEntity(singleWorkflow);
                discoveredCount++;
                _logger.LogDebug("Registered workflow from DI: {WorkflowName}", singleWorkflow.Name ?? singleWorkflow.GetType().Name);
            }

            _logger.LogInformation("📦 Discovered {Count} entities from DI container", discoveredCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering entities from DI container");
        }
    }
}
