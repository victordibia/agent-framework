using Microsoft.Agents.AI.DevUI.Core;
using Microsoft.Agents.AI.DevUI.Services;
using Microsoft.Agents.AI.DevUI.Models.Execution;
using Microsoft.Agents.AI.DevUI.Models.Session;
using Microsoft.Agents.AI.DevUI.Samples;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Test class to validate the refactored architecture
/// </summary>
public static class ArchitectureTest
{
    public static async Task RunTestsAsync()
    {
        Console.WriteLine("🚀 Testing Refactored .NET Agent Framework DevUI");
        Console.WriteLine("=================================================");

        // Create logger factory
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        try
        {
            await TestCoreServices(loggerFactory);
            await TestAgentExecution(loggerFactory);
            Console.WriteLine("\n✅ All tests passed! Architecture is working.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            throw;
        }
    }

    private static async Task TestCoreServices(ILoggerFactory loggerFactory)
    {
        Console.WriteLine("\n📋 Testing Core Services:");

        // Test Session Service
        var sessionService = new SessionService(loggerFactory.CreateLogger<SessionService>());
        var sessionId = await sessionService.CreateSessionAsync();
        Console.WriteLine($"✓ SessionService: Created session {sessionId[..8]}...");

        // Test request recording
        await sessionService.RecordRequestAsync(sessionId, new RequestRecord
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            EntityId = "test_agent",
            Method = "POST"
        });
        Console.WriteLine("✓ SessionService: Recorded request");

        // Test Entity Discovery Service
        var discoveryService = new EntityDiscoveryService(loggerFactory.CreateLogger<EntityDiscoveryService>());
        var weatherAgent = new WeatherAgent();
        discoveryService.RegisterInMemoryEntity(weatherAgent);

        var entities = discoveryService.ListEntities();
        Console.WriteLine($"✓ EntityDiscoveryService: Found {entities.Count} entities");

        // Test Message Mapper Service
        var mapperService = new MessageMapperService(loggerFactory.CreateLogger<MessageMapperService>());
        var testRequest = new DevUIExecutionRequest
        {
            Model = "test-model",
            Messages = new List<RequestMessage> { new() { Role = "user", Content = "Hello" } }
        };

        var response = mapperService.CreateChatCompletion(testRequest, "Hello from mapper!", sessionId);
        Console.WriteLine("✓ MessageMapperService: Created OpenAI response");

        Console.WriteLine("✅ Core services working!");
    }

    private static async Task TestAgentExecution(ILoggerFactory loggerFactory)
    {
        Console.WriteLine("\n🤖 Testing Agent Execution:");

        var weatherAgent = new WeatherAgent();
        var messages = new[]
        {
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "Weather in NYC?")
        };

        // Test non-streaming execution
        var response = await weatherAgent.RunAsync(messages);
        var responseText = response.Messages.First().Text ?? "";
        Console.WriteLine($"✓ Agent Response: {responseText[..Math.Min(50, responseText.Length)]}...");

        Console.WriteLine("✅ Agent execution working!");
    }
}