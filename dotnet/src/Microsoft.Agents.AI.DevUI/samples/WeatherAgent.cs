using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Microsoft.Agents.AI.DevUI.Samples;

/// <summary>
/// Weather agent that provides mock weather information
/// Follows the correct .NET Agent Framework patterns
/// </summary>
public class WeatherAgent : AIAgent
{
    public override string Id => "weather_agent";
    public override string Name => "Weather Agent";
    public override string Description => "Provides weather information for locations";

    public override AgentThread GetNewThread()
    {
        return new WeatherAgentThread();
    }

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return new WeatherAgentThread(serializedThread, jsonSerializerOptions);
    }

    public override async Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Create a thread if the user didn't supply one
        thread ??= GetNewThread();

        // Get the last message to extract location
        var lastMessage = messages.LastOrDefault()?.Text ?? "no location specified";

        // Mock weather response
        var response = GenerateWeatherResponse(lastMessage);
        var responseMessage = new ChatMessage(ChatRole.Assistant, response)
        {
            AuthorName = Name,
            MessageId = Guid.NewGuid().ToString()
        };

        // Notify thread of messages
        await NotifyThreadOfNewMessagesAsync(thread, messages.Concat([responseMessage]), cancellationToken);

        return new AgentRunResponse
        {
            AgentId = Id,
            ResponseId = Guid.NewGuid().ToString(),
            Messages = [responseMessage]
        };
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Create a thread if the user didn't supply one
        thread ??= GetNewThread();

        // Get the last message to extract location
        var lastMessage = messages.LastOrDefault()?.Text ?? "no location specified";
        var response = GenerateWeatherResponse(lastMessage);

        var responseMessage = new ChatMessage(ChatRole.Assistant, response)
        {
            AuthorName = Name,
            MessageId = Guid.NewGuid().ToString()
        };

        // Notify thread of messages
        await NotifyThreadOfNewMessagesAsync(thread, messages.Concat([responseMessage]), cancellationToken);

        // Stream the response character by character for demo
        foreach (char c in response)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new AgentRunResponseUpdate
            {
                AgentId = Id,
                AuthorName = Name,
                Role = ChatRole.Assistant,
                Contents = [new TextContent(c.ToString())],
                ResponseId = Guid.NewGuid().ToString(),
                MessageId = responseMessage.MessageId
            };

            // Small delay to make streaming visible
            await Task.Delay(50, cancellationToken);
        }
    }

    private string GenerateWeatherResponse(string location)
    {
        // Mock weather conditions
        var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Snowy", "Partly Cloudy" };
        var temperatures = new[] { "72°F", "68°F", "75°F", "65°F", "70°F" };

        var random = new Random();
        var condition = conditions[random.Next(conditions.Length)];
        var temp = temperatures[random.Next(temperatures.Length)];

        return $"🌤️ Weather for '{location}': {condition}, {temp}. This is a mock response from the Weather Agent.";
    }
}

/// <summary>
/// Custom thread for WeatherAgent
/// </summary>
public class WeatherAgentThread : AgentThread
{
    public WeatherAgentThread() : base()
    {
    }

    public WeatherAgentThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        : base()
    {
        // Custom deserialization logic can be added here if needed
    }
}