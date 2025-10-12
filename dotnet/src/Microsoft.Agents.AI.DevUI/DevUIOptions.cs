namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Configuration options for DevUI server
/// Can be configured via appsettings.json or programmatically
/// </summary>
public class DevUIOptions
{
    /// <summary>
    /// Port to run the DevUI server on. Default: 8080
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Host to bind the DevUI server to. Default: 127.0.0.1
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Directory to scan for entity files. Optional.
    /// </summary>
    public string? EntitiesDir { get; set; }

    /// <summary>
    /// Whether to automatically discover agents and workflows from the DI container.
    /// Default: true
    /// </summary>
    public bool DiscoverFromDI { get; set; } = true;

    /// <summary>
    /// Whether to enable the UI. Default: true
    /// </summary>
    public bool UiEnabled { get; set; } = true;

    /// <summary>
    /// Whether to automatically open browser on startup. Default: false
    /// </summary>
    public bool AutoOpen { get; set; }

    /// <summary>
    /// List of allowed CORS origins. Use "*" to allow all origins.
    /// Default: ["*"]
    /// </summary>
    public List<string> CorsOrigins { get; set; } = new() { "*" };

    /// <summary>
    /// Base path to mount DevUI endpoints. Default: "/devui"
    /// Example: "/debug" would make DevUI available at http://localhost:5000/debug
    /// </summary>
    public string BasePath { get; set; } = "/devui";

    /// <summary>
    /// Whether to run DevUI on a separate port (true) or integrate with the main app (false).
    /// Default: false (integrated mode)
    /// </summary>
    public bool UseSeperatePort { get; set; }

    /// <summary>
    /// Maximum number of concurrent conversations to track. Default: 1000
    /// </summary>
    public int MaxConversations { get; set; } = 1000;
}
