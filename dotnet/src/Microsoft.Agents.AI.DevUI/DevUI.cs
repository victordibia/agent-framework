using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Main entry point for Agent Framework DevUI - provides simple API to launch development server
/// </summary>
public static class DevUI
{
    /// <summary>
    /// Launch Agent Framework DevUI server with simple configuration
    /// </summary>
    /// <param name="entities">List of entities for in-memory registration</param>
    /// <param name="entitiesDir">Directory to scan for entities</param>
    /// <param name="port">Port to run server on</param>
    /// <param name="host">Host to bind server to</param>
    /// <param name="autoOpen">Whether to automatically open browser (not implemented yet)</param>
    /// <param name="corsOrigins">List of allowed CORS origins</param>
    /// <param name="uiEnabled">Whether to enable the UI</param>
    public static async Task ServeAsync(
        IEnumerable<object>? entities = null,
        string? entitiesDir = null,
        int port = 8080,
        string host = "127.0.0.1",
        bool autoOpen = false,
        List<string>? corsOrigins = null,
        bool uiEnabled = true)
    {
        var server = new DevUIServer(entitiesDir, port, host, corsOrigins, uiEnabled);

        if (entities != null)
        {
            server.RegisterEntities(entities.ToArray());
        }

        if (autoOpen)
        {
            // TODO: Implement browser opening
            Console.WriteLine($"Would open browser to http://{host}:{port}");
        }

        await server.RunAsync();
    }

    /// <summary>
    /// Create a server instance for more advanced configuration
    /// </summary>
    public static DevUIServer CreateServer(
        string? entitiesDir = null,
        int port = 8080,
        string host = "127.0.0.1",
        List<string>? corsOrigins = null,
        bool uiEnabled = true)
    {
        return new DevUIServer(entitiesDir, port, host, corsOrigins, uiEnabled);
    }
}