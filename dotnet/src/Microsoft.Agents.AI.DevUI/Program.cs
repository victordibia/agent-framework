using System.CommandLine;
using Microsoft.Agents.AI.DevUI;

// CLI Command setup
var entitiesDirOption = new Option<string?>("--entities-dir");
var portOption = new Option<int>("--port") { DefaultValueFactory = _ => 8080 };
var hostOption = new Option<string>("--host") { DefaultValueFactory = _ => "127.0.0.1" };
var autoOpenOption = new Option<bool>("--auto-open") { DefaultValueFactory = _ => false };

var rootCommand = new RootCommand("Agent Framework DevUI - Development server for .NET agents and workflows")
{
    entitiesDirOption,
    portOption,
    hostOption,
    autoOpenOption
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var entitiesDir = parseResult.GetValue(entitiesDirOption);
    var port = parseResult.GetValue(portOption);
    var host = parseResult.GetValue(hostOption);
    var autoOpen = parseResult.GetValue(autoOpenOption);

    Console.WriteLine("🚀 Starting Agent Framework DevUI for .NET");
    Console.WriteLine($"📁 Entities directory: {entitiesDir ?? "none (in-memory only)"}");
    Console.WriteLine($"🌐 Server: http://{host}:{port}");
    Console.WriteLine();

    try
    {
        // Load sample entities in-memory (don't use file discovery for samples)
        // This ensures entities are actually instantiated and can be executed
        object[]? entities = null;
        string? actualEntitiesDir = entitiesDir;

        if (entitiesDir?.Contains("samples", StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.WriteLine("📦 Loading built-in sample entities in-memory...");
            var weatherAgent = new Microsoft.Agents.AI.DevUI.Samples.WeatherAgent();
            var simpleWorkflow = await Microsoft.Agents.AI.DevUI.Samples.SimpleWorkflow.CreateAsync();
            entities = [weatherAgent, simpleWorkflow];

            // Don't use file discovery for samples since we're loading them in-memory
            actualEntitiesDir = null;
        }

        await DevUI.ServeAsync(
            entities: entities,
            entitiesDir: actualEntitiesDir,
            port: port,
            host: host ?? "127.0.0.1",
            autoOpen: autoOpen);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error starting server: {ex.Message}");
        Environment.Exit(1);
    }
});

// Add examples command
var examplesCommand = new Command("examples", "Show usage examples");
examplesCommand.SetAction((_, _) =>
{
    Console.WriteLine("Agent Framework DevUI Examples:");
    Console.WriteLine();
    Console.WriteLine("1. Start server with entities from directory:");
    Console.WriteLine("   dotnet run -- --entities-dir ./samples --port 8080");
    Console.WriteLine();
    Console.WriteLine("2. Start server for in-memory entities only:");
    Console.WriteLine("   dotnet run -- --port 8080");
    Console.WriteLine();
    Console.WriteLine("3. Start server with auto-open browser:");
    Console.WriteLine("   dotnet run -- --entities-dir ./samples --auto-open");
    Console.WriteLine();
    Console.WriteLine("4. Custom host and port:");
    Console.WriteLine("   dotnet run -- --host 0.0.0.0 --port 3000");
    Console.WriteLine();
    Console.WriteLine("The server provides OpenAI-compatible API endpoints:");
    Console.WriteLine("  • GET  /health              - Health check");
    Console.WriteLine("  • GET  /v1/entities         - List all entities");
    Console.WriteLine("  • GET  /v1/entities/{id}/info - Get entity details");
    Console.WriteLine("  • POST /v1/responses        - Execute entity (streaming/non-streaming)");

    return Task.CompletedTask;
});

rootCommand.Add(examplesCommand);

// Add capture command
var captureCommand = new Command("capture", "Run message capture tests");
captureCommand.SetAction(async (_, _) => await Microsoft.Agents.AI.DevUI.Tests.CaptureMessages.MainAsync(Array.Empty<string>()));
rootCommand.Add(captureCommand);

// Add compare command
var compareCommand = new Command("compare", "Compare Python vs .NET capture outputs");
compareCommand.SetAction(async (_, _) => await Microsoft.Agents.AI.DevUI.Tests.CompareOutputs.MainAsync(Array.Empty<string>()));
rootCommand.Add(compareCommand);

// Add explore command
var exploreCommand = new Command("explore", "Explore OpenAI Response types in .NET");
exploreCommand.SetAction(async (_, _) => await Microsoft.Agents.AI.DevUI.Tests.ExploreResponseTypes.MainAsync(Array.Empty<string>()));
rootCommand.Add(exploreCommand);

return await rootCommand.Parse(args).InvokeAsync();
