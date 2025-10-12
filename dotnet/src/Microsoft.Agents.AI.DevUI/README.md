# Microsoft.Agents.AI.DevUI

A development server for the .NET Agent Framework that provides OpenAI-compatible API endpoints for agents and workflows, designed to work with DevUI frontend.

> **Note**: This package follows the `Microsoft.Agents.AI.*` naming convention to align with other Agent Framework packages.

## Features

- 🔌 **OpenAI-Compatible API**: Implements OpenAI Responses API and Conversations API formats
- 🤖 **Agent & Workflow Support**: Discover and execute both agents and workflows
- 📡 **Streaming Support**: Full streaming execution with Server-Sent Events (SSE)
- 🔍 **Entity Discovery**: Automatic discovery from DI container, directories, or in-memory registration
- 💬 **Conversations API**: Complete OpenAI Conversations API implementation with items support
- 🔧 **DI Integration**: Seamless ASP.NET Core integration with AddDevUI() and UseDevUI()
- 🚀 **Dual Modes**: Run standalone (CLI) or integrated into existing ASP.NET Core apps

## Quick Start

### Standalone Mode (CLI)

```bash
cd dotnet/src/Microsoft.Agents.AI.DevUI
dotnet build
dotnet run -- --entities-dir samples --port 8071
```

Access DevUI at: `http://localhost:8071/`

### Integrated Mode (ASP.NET Core)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register your agents
builder.Services.AddSingleton<AIAgent, WeatherAgent>();

// Add DevUI
builder.Services.AddDevUI();

var app = builder.Build();
app.UseDevUI();
app.MapControllers();
app.Run();
```

Access DevUI at: `http://localhost:8080/devui`

See [INTEGRATION_EXAMPLE.md](Examples/INTEGRATION_EXAMPLE.md) for more integration patterns.

### 2. Test the API

```bash
# Check health
curl http://localhost:8080/health

# List entities
curl http://localhost:8080/v1/entities

# Execute entity (non-streaming)
curl -X POST http://localhost:8080/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "model": "agent-framework",
    "messages": [{"role": "user", "content": "What is the weather in San Francisco?"}],
    "stream": false,
    "extra_body": {
      "entity_id": "agent_weatheragent"
    }
  }'

# Execute entity (streaming)
curl -X POST http://localhost:8080/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "model": "agent-framework",
    "messages": [{"role": "user", "content": "What is the weather in New York?"}],
    "stream": true,
    "extra_body": {
      "entity_id": "agent_weatheragent"
    }
  }'
```

## API Endpoints

### Core Endpoints

- `GET /health` - Health check with entity count
- `GET /v1/entities` - List all discovered entities
- `GET /v1/entities/{id}/info` - Get detailed entity information
- `POST /v1/responses` - Execute entity (supports streaming)

### Conversations API

- `POST /v1/conversations` - Create a new conversation
- `GET /v1/conversations` - List conversations (with agent_id filter)
- `GET /v1/conversations/{id}` - Get conversation details
- `POST /v1/conversations/{id}` - Update conversation metadata
- `DELETE /v1/conversations/{id}` - Delete a conversation
- `POST /v1/conversations/{id}/items` - Add conversation items
- `GET /v1/conversations/{id}/items` - List conversation items
- `GET /v1/conversations/{id}/items/{itemId}` - Get specific item

## Directory Structure

DevUI discovers entities using **one-level scanning only** (matching Python's behavior) to prevent discovering unintended files. Two structures are supported:

### Option 1: Flat Structure (Simple)

```
samples/
├── WeatherAgent.cs              ← Discovered
├── SpamDetectionWorkflow.cs     ← Discovered
```

### Option 2: Folder-Based Structure (Organized)

```
entities/
├── weather_agent/          ← Folder name becomes entity ID
│   └── WeatherAgent.cs     ← Agent implementation
├── joke_agent/
│   └── JokeAgent.cs
└── my_workflow/
    └── MyWorkflow.cs
```

**Notes:**

- Only scans **one level deep** - nested subdirectories are ignored
- Skips hidden folders (`.git`), build folders (`bin`, `obj`), and `__pycache__`
- Folder-based structure provides better organization for complex entities

## Creating Entities

### Agent Example

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Agents;
using System.Runtime.CompilerServices;

namespace Microsoft.Agents.AI.DevUI.Samples;

public class WeatherAgent : AIAgent
{
    public override string Id => "weather_agent";
    public override string Name => "Weather Agent";
    public override string Description => "Provides weather information for locations";

    public override async Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = messages.LastOrDefault()?.Text ?? "no location specified";
        var response = $"🌤️ Weather for '{lastMessage}': Sunny, 72°F. This is a mock response from the Weather Agent.";

        var chatMessage = new ChatMessage(ChatRole.Assistant, response);
        thread ??= GetNewThread();

        return new AgentRunResponse(chatMessage);
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastMessage = messages.LastOrDefault()?.Text ?? "no location specified";
        var response = $"🌤️ Weather for '{lastMessage}': Sunny, 72°F. This is a mock streaming response from the Weather Agent.";

        foreach (char c in response)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new AgentRunResponseUpdate(ChatRole.Assistant, c.ToString());
            await Task.Delay(50, cancellationToken);
        }
    }
}
```

### Workflow Example

See [samples/SpamDetectionWorkflow.cs](samples/SpamDetectionWorkflow.cs) for a comprehensive workflow example with:
- 5-step processing pipeline
- Multiple executors with branching logic
- Realistic processing delays
- Email spam detection and handling

## Integration Examples

### Basic DI Integration

```csharp
// Register agents in DI
builder.Services.AddSingleton<AIAgent, WeatherAgent>();

// Add DevUI with default config
builder.Services.AddDevUI();

var app = builder.Build();
app.UseDevUI();
app.MapControllers();
```

### Configuration from appsettings.json

```csharp
builder.Services.AddDevUI(builder.Configuration.GetSection("DevUI"));
```

```json
{
  "DevUI": {
    "Port": 8071,
    "BasePath": "/devui",
    "DiscoverFromDI": true,
    "AutoOpen": true
  }
}
```

### Programmatic Configuration

```csharp
builder.Services.AddDevUI(options =>
{
    options.Port = 8071;
    options.BasePath = "/debug";
    options.EntitiesDir = "./agents";
    options.CorsOrigins = new List<string> { "http://localhost:3000" };
});
```

See [Examples/INTEGRATION_EXAMPLE.md](Examples/INTEGRATION_EXAMPLE.md) for complete integration patterns.

## CLI Options

```bash
# From the DevUI directory
cd <repo-root>/dotnet/src/Microsoft.Agents.AI.DevUI
dotnet run -- [options]

Options:
  --entities-dir <path>    Directory to scan for entities
  --port <number>          Port to run server on (default: 8080)
  --host <string>          Host to bind to (default: 127.0.0.1)
  --auto-open              Auto-open browser (default: false)

Commands:
  examples                 Show usage examples

Examples:
  dotnet run -- --help                                # Show help
  dotnet run -- examples                              # Show examples
  dotnet run -- --entities-dir samples --port 8080    # Basic usage
  dotnet run -- --host 0.0.0.0 --port 3000           # Custom host/port
```

## Integration with Agent Framework

This package is part of the Microsoft .NET Agent Framework and integrates with:

- **Microsoft.Extensions.AI.Agents.Abstractions**: Core agent abstractions
- **Microsoft.Agents.Workflows**: Workflow execution engine
- **Microsoft.Extensions.AI**: AI abstractions and types

### Dependencies

```xml
<ProjectReference Include="../Microsoft.Extensions.AI.Agents.Abstractions/Microsoft.Extensions.AI.Agents.Abstractions.csproj" />
<ProjectReference Include="../Microsoft.Agents.Workflows/Microsoft.Agents.Workflows.csproj" />
<PackageReference Include="Microsoft.Extensions.AI" />
<PackageReference Include="OpenAI" />
<PackageReference Include="System.CommandLine" />
```

## Development

### Project Structure

```
Microsoft.Agents.AI.DevUI/
├── Controllers/           # API controllers
├── Services/             # Core services (discovery, execution, conversations)
├── Models/              # API models and DTOs
├── Extensions/          # DI extension methods (AddDevUI, UseDevUI)
├── Examples/            # Integration examples and guides
├── samples/             # Sample agents and workflows
├── DevUI.cs            # Static helper for standalone mode
├── DevUIServer.cs      # Server builder
├── DevUIOptions.cs     # Configuration options
└── Program.cs          # CLI entry point
```

### Entity Discovery

The server discovers entities in three ways:

1. **DI Container**: Automatically discovers agents/workflows registered in the service collection (enabled by default)
2. **Directory Scanning**: Scans `.cs` files for agent/workflow patterns (flat or folder-based structure)
3. **In-Memory Registration**: Register entities programmatically in standalone mode

**DI Discovery Example:**
```csharp
// Agents registered in DI are automatically discovered
builder.Services.AddSingleton<AIAgent, WeatherAgent>();
builder.Services.AddDevUI(options => options.DiscoverFromDI = true);
```

All directory scanning is **one-level only** to prevent discovering unintended files in nested directories.

### UI Serving

The DevUI server automatically serves the React frontend from the `/` route:

**Features**:

- **Static File Serving**: All UI assets served from `ui/` directory
- **SPA Fallback**: Non-API routes redirect to `index.html` for client-side routing
- **API Preservation**: API routes (`/v1/*`, `/health`) remain accessible
- **Auto-Detection**: Falls back to API-only mode if UI files are missing

**UI Directory Structure**:

```
Microsoft.Agents.AI.DevUI/
├── ui/
│   ├── index.html         # React app entry point
│   ├── vite.svg          # Favicon
│   └── assets/           # Built React bundles
│       ├── index-*.js    # JavaScript bundle
│       └── index-*.css   # CSS bundle
```

**Access Points**:

- **UI**: `http://localhost:8080/` (React DevUI)
- **API**: `http://localhost:8080/v1/entities` (OpenAI-compatible)
- **Health**: `http://localhost:8080/health` (Server status)

### Execution Architecture

The `ExecutionService` provides unified execution for both agents and workflows:

**Agent Execution**:

- Uses `AIAgent.RunAsync()` for real agent execution
- Converts between OpenAI request format and framework `ChatMessage[]`
- Maps `AgentRunResponse` to OpenAI-compatible format

**Workflow Execution**:

- Uses `InProcessExecution.RunAsync()` for real workflow execution
- Supports `Workflow<string>` and `Workflow<ChatMessage[]>` input types
- Maps workflow events to readable text responses (see Event Mapping table below)

### Event Mapping

Workflow events are converted to human-readable text for OpenAI-compatible responses:

| Event Type                | Description                      | Output Format                                | Example                                        |
| ------------------------- | -------------------------------- | -------------------------------------------- | ---------------------------------------------- |
| `AgentRunResponseEvent`   | Agent produces a response        | `Agent Response: {response.Text}`            | `Agent Response: The weather is sunny, 72°F`   |
| `AgentRunUpdateEvent`     | Agent produces streaming update  | `Agent Update: {update.Text}`                | `Agent Update: Processing weather data...`     |
| `WorkflowCompletedEvent`  | Workflow execution completed     | `Workflow completed successfully`            | `Workflow completed successfully`              |
| `WorkflowStartedEvent`    | Workflow execution started       | `Workflow started: {message}`                | `Workflow started: Processing user input`      |
| `WorkflowErrorEvent`      | Workflow encountered an error    | `Workflow error: {exception.Message}`        | `Workflow error: Null reference exception`     |
| `WorkflowWarningEvent`    | Workflow warning occurred        | `Workflow warning: {message}`                | `Workflow warning: Connection timeout`         |
| `ExecutorCompletedEvent`  | Executor finished successfully   | `Executor '{executorId}' completed`          | `Executor 'weather-processor' completed`       |
| `ExecutorFailureEvent`    | Executor failed during execution | `Executor '{executorId}' failed: {error}`    | `Executor 'data-fetcher' failed: API timeout`  |
| `ExecutorInvokedEvent`    | Executor was called              | `Executor '{executorId}' invoked: {message}` | `Executor 'validator' invoked: Checking input` |
| `SuperStepStartedEvent`   | Workflow step started            | `Step {stepNumber} started`                  | `Step 1 started`                               |
| `SuperStepCompletedEvent` | Workflow step completed          | `Step {stepNumber} completed`                | `Step 1 completed`                             |
| `RequestInfoEvent`        | External request received        | `External request: {request}`                | `External request: User input required`        |
| **Other Events**          | Any unmapped event               | `{EventType}: {data}`                        | `CustomEvent: Additional data`                 |

**Note**: All events include their raw data in the `Data` property, which is converted to string representation in the output. Events without meaningful data show "No data" in the output.

### Building

```bash
# From the DevUI directory
cd <repo-root>/dotnet/src/Microsoft.Agents.AI.DevUI
dotnet build

# From the repository root
dotnet build dotnet/src/Microsoft.Agents.AI.DevUI/Microsoft.Agents.AI.DevUI.csproj

# Build all framework packages
cd <repo-root>/dotnet
dotnet build
```

## Current Status

- ✅ **Complete API**: All endpoints implemented (entities + full conversations API)
- ✅ **DI Integration**: Full ASP.NET Core dependency injection support
- ✅ **Framework Integration**: Uses real agent framework types and execution
- ✅ **Conversations API**: Complete OpenAI Conversations API with items
- ✅ **Entity Discovery**: DI container, file-based, and in-memory
- ✅ **Streaming Support**: SSE streaming with OpenAI Responses API format
- ✅ **Real Agent Execution**: Executes actual agents using Agent Framework
- ✅ **Real Workflow Execution**: Executes actual workflows with event mapping
- ✅ **UI Serving**: Serves React DevUI from base path with SPA fallback support
- ✅ **Dual Modes**: Standalone CLI or integrated into ASP.NET Core apps

## Future Enhancements

- [ ] **Testing**: Unit and integration tests for all services
- [ ] **Dynamic Assembly Loading**: Runtime compilation for entity discovery
- [ ] **Authentication/Authorization**: Security layer for production use
- [ ] **Hot Reload**: Watch file changes for entities
- [ ] **Persistent Storage**: Database backend for conversations
- [ ] **UI Build Integration**: Automate frontend build with dotnet build

## Troubleshooting

### Port Already in Use

```bash
# Try a different port (from DevUI directory)
dotnet run -- --port 8081

# Find what's using the port (macOS/Linux)
lsof -i :8080
```

### Entity Discovery Issues

- Check that `.cs` files contain `AIAgent` or `Workflow` patterns
- Verify the `--entities-dir` path is correct
- Look at server logs for discovery information

## License

Copyright (c) Microsoft. All rights reserved.
