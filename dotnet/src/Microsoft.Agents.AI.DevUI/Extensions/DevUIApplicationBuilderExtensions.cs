using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Microsoft.Agents.AI.DevUI.Extensions;

/// <summary>
/// Extension methods for configuring DevUI in the middleware pipeline
/// </summary>
public static class DevUIApplicationBuilderExtensions
{
    /// <summary>
    /// Maps DevUI endpoints and serves the UI at the configured base path
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseDevUI(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<DevUIOptions>>()
            .Value;

        return app.UseDevUI(options.BasePath);
    }

    /// <summary>
    /// Maps DevUI endpoints and serves the UI at a specific base path
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="basePath">Base path to mount DevUI (e.g., "/devui" or "/debug")</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseDevUI(this IApplicationBuilder app, string basePath)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<DevUIOptions>>()
            .Value;

        // Normalize base path
        basePath = basePath.TrimEnd('/');
        if (!basePath.StartsWith('/'))
        {
            basePath = "/" + basePath;
        }

        // Serve static files for the UI if enabled
        if (options.UiEnabled)
        {
            var uiDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            if (Directory.Exists(uiDirectory))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(uiDirectory),
                    RequestPath = basePath
                });

                Console.WriteLine($"📱 DevUI available at: http://{options.Host}:{options.Port}{basePath}");
            }
            else
            {
                Console.WriteLine($"⚠️  DevUI UI files not found at: {uiDirectory}");
            }
        }

        return app;
    }

    /// <summary>
    /// Maps DevUI API endpoints with route prefix
    /// For use with minimal APIs - maps endpoints at /devui/v1/*
    /// </summary>
    /// <param name="app">The endpoint route builder</param>
    /// <param name="basePath">Base path to mount DevUI (default: "/devui")</param>
    /// <returns>The endpoint route builder for chaining</returns>
    public static IEndpointRouteBuilder MapDevUI(this IEndpointRouteBuilder app, string basePath = "/devui")
    {
        // Normalize base path
        basePath = basePath.TrimEnd('/');
        if (!basePath.StartsWith('/'))
        {
            basePath = "/" + basePath;
        }

        // Note: DevUI uses Controllers, so endpoints are automatically mapped via MapControllers()
        // This method is here for consistency with other .UseXxx() patterns
        // The actual routing is handled by the Controllers with route attributes

        var options = app.ServiceProvider
            .GetRequiredService<IOptions<DevUIOptions>>()
            .Value;

        Console.WriteLine("🔌 DevUI API endpoints:");
        Console.WriteLine($"   GET  {basePath}/v1/entities");
        Console.WriteLine($"   GET  {basePath}/v1/entities/{{id}}/info");
        Console.WriteLine($"   POST {basePath}/v1/responses");
        Console.WriteLine($"   GET  {basePath}/v1/conversations");
        Console.WriteLine($"   GET  {basePath}/health");

        return app;
    }
}
