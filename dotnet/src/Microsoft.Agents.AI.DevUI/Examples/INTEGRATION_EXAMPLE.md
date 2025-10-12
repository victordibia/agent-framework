# DevUI Integration Guide

DevUI for .NET provides a web-based debugging interface for AI agents and workflows. It integrates seamlessly into ASP.NET Core applications through dependency injection, offering:

- **Agent Discovery**: Automatically finds agents and workflows registered in your DI container
- **Interactive Testing**: Test agents with a conversational UI at runtime
- **Conversation Management**: View and replay conversation histories
- **API Endpoints**: RESTful APIs for agent execution and metadata
- **Flexible Configuration**: Configure via `appsettings.json` or programmatically
- **Development Focus**: Optionally enable only in development environments

## Integration Patterns

### Basic Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register agents
builder.Services.AddSingleton<AIAgent, WeatherAgent>();

// Add DevUI
builder.Services.AddDevUI();

var app = builder.Build();
app.UseDevUI();
app.MapControllers();
app.Run();
```

Access at: `http://localhost:8080/devui`

---

### Configuration from appsettings.json

```csharp
builder.Services.AddDevUI(builder.Configuration.GetSection("DevUI"));
```

**appsettings.json:**
```json
{
  "DevUI": {
    "Port": 5000,
    "BasePath": "/devui",
    "DiscoverFromDI": true,
    "AutoOpen": true
  }
}
```

---

### Programmatic Configuration

```csharp
builder.Services.AddDevUI(options =>
{
    options.Port = 8071;
    options.BasePath = "/debug";
    options.EntitiesDir = "./agents";  // Discover from file system
    options.CorsOrigins = new List<string> { "http://localhost:3000" };
});

app.UseDevUI("/debug");
```

---

### Multiple Agents and Workflows

```csharp
// Register multiple agents
builder.Services.AddSingleton<AIAgent, WeatherAgent>();
builder.Services.AddSingleton<AIAgent, CustomerServiceAgent>();

// Register workflows
builder.Services.AddSingleton<Workflow>(sp =>
    SpamDetectionWorkflow.CreateAsync().Result);

builder.Services.AddDevUI();
```

---

### Agents with Dependencies

```csharp
// Register agent with factory pattern
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var db = sp.GetRequiredService<ICustomerDatabase>();
    var logger = sp.GetRequiredService<ILogger<CustomerAgent>>();
    return new CustomerAgent(db, logger);
});
```

---

### Development-Only Mode

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevUI(options => options.AutoOpen = true);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDevUI();
}
```

---

### Standalone Server

Run DevUI as a separate process:

```bash
dotnet run -- --entities-dir ./agents --port 8071
```

---

## Configuration Options

| Property | Default | Description |
|----------|---------|-------------|
| `Port` | 8080 | Port for DevUI server |
| `Host` | "127.0.0.1" | Host to bind to |
| `BasePath` | "/devui" | URL path for DevUI |
| `DiscoverFromDI` | true | Auto-discover agents from DI container |
| `EntitiesDir` | null | Directory to scan for agent files |
| `AutoOpen` | false | Open browser on startup |
| `CorsOrigins` | ["*"] | Allowed CORS origins |
| `MaxConversations` | 1000 | Max conversations to track |

## API Endpoints

All endpoints are prefixed with `BasePath` (default: `/devui`):

- `GET /v1/entities` - List agents and workflows
- `GET /v1/entities/{id}/info` - Get entity metadata
- `POST /v1/responses` - Execute agent (supports streaming)
- `GET /v1/conversations` - List conversations
- `GET /v1/conversations/{id}` - Get conversation details
- `GET /health` - Health check

## How DI Integration Works

DevUI integrates with ASP.NET Core's dependency injection:

1. **Agent Discovery**: DevUI scans the DI container for `AIAgent` and `Workflow` registrations
2. **Middleware**: `UseDevUI()` adds middleware to serve the UI and handle API requests
3. **Controllers**: `MapControllers()` registers DevUI's API endpoints
4. **Lifecycle**: Agents are resolved from DI per request, respecting their registered lifetime (Singleton, Scoped, Transient)

**Required setup:**
```csharp
// 1. Register agents in DI
builder.Services.AddSingleton<AIAgent, WeatherAgent>();

// 2. Add DevUI services
builder.Services.AddDevUI();

// 3. Enable DevUI middleware and endpoints
app.UseDevUI();
app.MapControllers();
```

**Works with:**
- Minimal APIs
- MVC/Controllers
- Existing middleware pipelines
- All standard DI lifetimes

## Troubleshooting

**Agents not found:** Ensure `DiscoverFromDI = true` and agents registered as `AddSingleton<AIAgent, T>()`

**UI not loading:** Check `ui/` folder exists in output directory

**CORS errors:** Update `CorsOrigins` in options
