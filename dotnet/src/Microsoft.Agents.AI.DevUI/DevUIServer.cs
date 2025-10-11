using Microsoft.Agents.AI.DevUI.Services;
using Microsoft.Agents.AI.DevUI.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.FileProviders;

namespace Microsoft.Agents.AI.DevUI;

public class DevUIServer
{
    private readonly string? _entitiesDir;
    private readonly int _port;
    private readonly string _host;
    private readonly List<string> _corsOrigins;
    private readonly bool _uiEnabled;
    private readonly List<object> _inMemoryEntities = new();

    public DevUIServer(
        string? entitiesDir = null,
        int port = 8080,
        string host = "127.0.0.1",
        List<string>? corsOrigins = null,
        bool uiEnabled = true)
    {
        _entitiesDir = entitiesDir;
        _port = port;
        _host = host;
        _corsOrigins = corsOrigins ?? new List<string> { "*" };
        _uiEnabled = uiEnabled;
    }

    public void RegisterEntities(params object[] entities)
    {
        _inMemoryEntities.AddRange(entities);
    }

    public WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();

        // Add services
        builder.Services.AddControllers();
        // TODO: Add Swagger when available in central packages
        // builder.Services.AddEndpointsApiExplorer();
        // builder.Services.AddSwaggerGen();

        // Add CORS
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (_corsOrigins.Contains("*"))
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                }
                else
                {
                    policy.WithOrigins(_corsOrigins.ToArray())
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
            });
        });

        // Add our services
        builder.Services.AddSingleton<EntityDiscoveryService>();
        builder.Services.AddSingleton<MessageMapperService>();
        builder.Services.AddSingleton<ExecutionService>();
        builder.Services.AddSingleton<ConversationService>();

        var app = builder.Build();

        // Configure pipeline
        // TODO: Add Swagger when available
        // if (app.Environment.IsDevelopment())
        // {
        //     app.UseSwagger();
        //     app.UseSwaggerUI();
        // }

        app.UseCors();

        // Serve UI static files if enabled
        if (_uiEnabled)
        {
            var uiDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            if (Directory.Exists(uiDirectory))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(uiDirectory),
                    RequestPath = ""
                });
            }
        }

        app.UseRouting();
        app.MapControllers();

        // Initialize entity discovery
        var discoveryService = app.Services.GetRequiredService<EntityDiscoveryService>();

        // Discover entities from directory
        if (!string.IsNullOrEmpty(_entitiesDir))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await discoveryService.DiscoverEntitiesFromDirectoryAsync(_entitiesDir);
                }
                catch (Exception ex)
                {
                    var logger = app.Services.GetRequiredService<ILogger<DevUIServer>>();
                    logger.LogError(ex, "Failed to discover entities from directory");
                }
            });
        }

        // Register in-memory entities
        foreach (var entity in _inMemoryEntities)
        {
            discoveryService.RegisterInMemoryEntity(entity);
        }

        // Serve UI if enabled
        if (_uiEnabled)
        {
            var uiDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            var indexPath = Path.Combine(uiDirectory, "index.html");

            if (File.Exists(indexPath))
            {
                // Serve index.html for the root route and SPA fallback
                app.MapGet("/", async context =>
                {
                    context.Response.ContentType = "text/html";
                    await context.Response.SendFileAsync(indexPath);
                });

                // Fallback to index.html for client-side routing (SPA)
                app.MapFallback(async context =>
                {
                    // Only serve index.html for non-API routes
                    if (!context.Request.Path.StartsWithSegments("/v1") &&
                        !context.Request.Path.StartsWithSegments("/health"))
                    {
                        context.Response.ContentType = "text/html";
                        await context.Response.SendFileAsync(indexPath);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                });
            }
            else
            {
                // Fallback endpoint when no UI files are present
                app.MapGet("/", () => new
                {
                    message = "Agent Framework DevUI Server",
                    status = "UI files not found - API only mode",
                    endpoints = new
                    {
                        health = "/health",
                        entities = "/v1/entities",
                        responses = "/v1/responses"
                    }
                });
            }
        }

        return app;
    }

    public async Task RunAsync()
    {
        var app = CreateApp();

        var logger = app.Services.GetRequiredService<ILogger<DevUIServer>>();
        logger.LogInformation("Starting Agent Framework DevUI on {Host}:{Port}", _host, _port);

        await app.RunAsync($"http://{_host}:{_port}");
    }
}