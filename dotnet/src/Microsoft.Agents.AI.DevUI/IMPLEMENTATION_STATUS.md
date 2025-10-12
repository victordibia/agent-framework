# Microsoft.Agents.AI.DevUI - Implementation Status

## Package Naming
**Package Name**: `Microsoft.Agents.AI.DevUI`
**Namespace**: `Microsoft.Agents.AI.DevUI`

This package follows the `Microsoft.Agents.AI.*` naming convention to align with other Agent Framework packages (Microsoft.Agents.AI.Workflows, Microsoft.Agents.AI.OpenAI, etc.).

## Overview
Successfully ported Python DevUI to .NET, implementing OpenAI Responses API format for streaming agent and workflow execution.

## ✅ Completed Features

### 1. Core Infrastructure
- **EntityDiscoveryService**: Discovers agents and workflows from in-memory (DI) and directory sources
- **ExecutionService**: Unified service for executing agents and workflows
- **MessageMapperService**: Converts Agent Framework events to OpenAI Responses API format
- **ConversationService**: Manages conversations using OpenAI Conversations API format (wraps AgentThread internally)
- **DevUIController**: REST API endpoints with full Conversations API support
- **DevUIHostedService**: Background service for discovering entities from DI container
- **DI Extensions**: AddDevUI() and UseDevUI() for seamless ASP.NET Core integration

### 2. API Endpoints

#### Entity Endpoints
- `GET /v1/entities` - List all discovered entities
- `GET /v1/entities/{id}/info` - Get entity details (includes workflow_dump for visualization)
- `POST /v1/responses` - Execute entity (streaming or non-streaming)
- `GET /health` - Health check

#### Conversations API (Full Implementation)
- `POST /v1/conversations` - Create conversation
- `GET /v1/conversations` - List conversations (with agent_id filter)
- `GET /v1/conversations/{id}` - Get conversation details
- `POST /v1/conversations/{id}` - Update conversation metadata
- `DELETE /v1/conversations/{id}` - Delete conversation
- `POST /v1/conversations/{id}/items` - Add conversation items
- `GET /v1/conversations/{id}/items` - List conversation items
- `GET /v1/conversations/{id}/items/{itemId}` - Get specific item

### 3. OpenAI Responses API Format
Successfully implemented streaming events matching Python's format:

#### Agent Events
- ✅ `response.output_text.delta` - Text streaming (character-by-character or word-by-word)
- ✅ `response.function_call_arguments.delta` - Function call arguments
- ✅ `response.function_result.complete` - Function execution results
- ✅ `response.usage.complete` - Token usage information
- ✅ `[DONE]` - Stream termination

#### Workflow Events
- ✅ `response.workflow_event.complete` - Workflow event streaming
  - WorkflowStartedEvent
  - ExecutorInvokedEvent
  - ExecutorCompletedEvent
  - WorkflowOutputEvent
  - WorkflowErrorEvent
  - WorkflowWarningEvent

### 4. JSON Serialization
- ✅ Snake_case property names using `[JsonPropertyName]` attributes
- ✅ Support for both `input` (Responses API) and `messages` (Chat Completions) formats
- ✅ `extra_body` dictionary for metadata (entity_id, etc.)

### 5. Real Agent Framework Integration
- ✅ Running actual `AIAgent` implementations (WeatherAgent sample)
- ✅ Running actual `Workflow` implementations (SpamDetectionWorkflow sample)
- ✅ Proper streaming with `AgentRunResponseUpdate` events
- ✅ Workflow event propagation via `Run.OutgoingEvents`
- ✅ DI-based entity discovery (auto-discovers agents/workflows from service collection)
- ✅ ASP.NET Core middleware integration (UseDevUI, MapControllers)

## 🔧 Key Implementation Details

### MessageMapperService
Maps Agent Framework content types to Responses API events:
- `TextContent` → `response.output_text.delta`
- `FunctionCallContent` → `response.function_call_arguments.delta`
- `FunctionResultContent` → `response.function_result.complete`
- `UsageContent` → `response.usage.complete`
- `WorkflowEvent` → `response.workflow_event.complete`

### ExecutionService
Two streaming methods:
1. **ExecuteAgentStreamingAsync**: Streams `AgentRunResponseUpdate` events
2. **ExecuteWorkflowStreamingAsync**: Streams `WorkflowEvent` events

Both use MessageMapperService for conversion to Responses API format.

### Entity Discovery
Discovers entities from:
1. In-memory registrations (programmatic)
2. Directory scanning (C# files with agents/workflows)

## 📊 Comparison with Python

| Feature | Python DevUI | .NET DevUI | Status |
|---------|-------------|-----------|--------|
| Entity Discovery | ✅ | ✅ | ✅ Complete |
| Agent Streaming | ✅ | ✅ | ✅ Complete |
| Workflow Streaming | ✅ | ✅ | ✅ Complete |
| Responses API Format | ✅ | ✅ | ✅ Complete |
| SSE Streaming | ✅ | ✅ | ✅ Complete |
| Thread Management | ✅ | ✅ | ✅ Complete |
| Frontend Compatibility | ✅ | 🟡 | ⏳ Needs Testing |

## 🧪 Testing Status

### Unit/Integration Tests
- ⏳ CaptureMessages.cs - Captures streaming output for comparison
- ⏳ CompareOutputs.cs - Compares .NET vs Python output
- ⏳ ExploreResponseTypes.cs - Investigates OpenAI SDK types

### Manual Testing Needed
1. ✅ Agent streaming with WeatherAgent
2. ⏳ Workflow streaming with SimpleWorkflow
3. ⏳ Frontend UI integration
4. ⏳ Compare output with Python's `captured_messages/entities_stream_events.json`

## 🗂️ File Structure

### Core Services
- `Services/EntityDiscoveryService.cs` - Entity discovery (DI + file system)
- `Services/ExecutionService.cs` - Agent/workflow execution
- `Services/MessageMapperService.cs` - Event format conversion
- `Services/ConversationService.cs` - Conversation and thread management (wraps AgentThread)
- `Services/DevUIHostedService.cs` - Background service for DI entity discovery

### Models
- `Models/DiscoveryModels.cs` - Entity discovery DTOs
- `Models/Execution/ExecutionModels.cs` - Execution request/response
- `Models/Execution/ResponseEvents.cs` - Responses API event types
- `Models/ConversationModels.cs` - Conversations API DTOs

### Controllers
- `Controllers/DevUIController.cs` - REST API endpoints (entities + conversations)

### DI Integration
- `Extensions/DevUIServiceCollectionExtensions.cs` - AddDevUI() methods
- `Extensions/DevUIApplicationBuilderExtensions.cs` - UseDevUI() and MapDevUI() methods
- `DevUIOptions.cs` - Configuration options

### Entry Points
- `Program.cs` - CLI entry point (standalone mode)
- `DevUI.cs` - Static helper for ServeAsync
- `DevUIServer.cs` - Server builder and configuration

### Samples
- `samples/WeatherAgent.cs` - Sample AIAgent implementation
- `samples/SpamDetectionWorkflow.cs` - Comprehensive workflow with 5 steps and branching logic

## 🚀 How to Run

### Standalone Mode (CLI)

```bash
# Start server with sample entities
cd dotnet/src/Microsoft.Agents.AI.DevUI
dotnet run -- --entities-dir samples --port 8081

# Test entities endpoint
curl http://127.0.0.1:8081/v1/entities | python3 -m json.tool

# Test streaming execution
curl -X POST http://127.0.0.1:8081/v1/responses \
  -H "Content-Type: application/json" \
  -d '{"model": "agent-framework", "messages": [{"role": "user", "content": "What is the weather?"}], "stream": true, "extra_body": {"entity_id": "agent_weatheragent"}}'
```

### Integrated Mode (ASP.NET Core)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register agents
builder.Services.AddSingleton<AIAgent, WeatherAgent>();

// Add DevUI
builder.Services.AddDevUI(options =>
{
    options.Port = 8071;
    options.BasePath = "/devui";
    options.DiscoverFromDI = true;
});

var app = builder.Build();
app.UseDevUI();
app.MapControllers();
app.Run();
```

## 📝 Known Differences from Python

1. **Streaming Granularity**:
   - Python: Word-by-word or token-by-token (OpenAI API behavior)
   - .NET: Character-by-character (WeatherAgent sample implementation)
   - *This is an Agent Framework design choice, not a DevUI limitation*

2. **content_index**:
   - Increments per chunk in both implementations
   - Minor differences in how chunks are defined

## 🔄 Recent Changes

### Current Implementation (Latest)
1. ✅ **DI Integration Complete**: Full ASP.NET Core dependency injection support
   - AddDevUI() extension methods (configuration + programmatic)
   - UseDevUI() middleware integration
   - DevUIHostedService for auto-discovery from DI container
2. ✅ **Conversations API Complete**: Full OpenAI Conversations API implementation
   - Replaced ThreadService with ConversationService
   - Complete CRUD operations for conversations and items
   - AgentThread wrapped internally for compatibility
3. ✅ **Updated Samples**:
   - Removed SimpleWorkflow.cs (deleted)
   - Added SpamDetectionWorkflow.cs (comprehensive 5-step workflow)
4. ✅ **Workflow Visualization**: workflow_dump serialization for frontend
5. ✅ **Responses API Format**: All streaming events use proper SSE format

### Previous Sessions
1. ✅ Built ResponseEvents types matching Python
2. ✅ Fixed workflow streaming to use Responses API format
3. ✅ Supported both `input` and `messages` request formats
4. ✅ Fixed Unicode/emoji handling in streaming

## 🎯 Next Steps

1. **Frontend Integration**: Test with DevUI frontend to ensure full compatibility
2. **Testing**: Add unit and integration tests for all services
3. **Documentation**: Add XML documentation comments throughout
4. **Performance**: Optimize streaming and conversation storage for production
5. **UI Build Integration**: Automate UI build as part of dotnet build process

## 📚 Reference

- .NET Implementation: `dotnet/src/Microsoft.Agents.AI.DevUI/`
- Python Reference: Python DevUI package (for API format compatibility)
- Integration Examples: `Examples/INTEGRATION_EXAMPLE.md`
