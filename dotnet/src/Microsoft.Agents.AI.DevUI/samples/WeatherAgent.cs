using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ComponentModel;

namespace Microsoft.Agents.AI.DevUI.Samples;

/// <summary>
/// Weather agent powered by Azure OpenAI Chat Completion with weather tools
/// This agent provides weather information using AI with function calling
///
/// Configuration:
/// - AZURE_OPENAI_ENDPOINT: Your Azure OpenAI endpoint (required)
/// - AZURE_OPENAI_DEPLOYMENT_NAME: Deployment name (optional, defaults to gpt-4o-mini)
/// - AZURE_OPENAI_API_KEY: API key (optional if using Azure AD auth)
/// </summary>
public class WeatherAgent : DelegatingAIAgent
{
    private static ChatClientAgent? s_agent;
    private static bool s_initializationAttempted;
    private static Exception? s_initializationError;

    public WeatherAgent() : base(GetOrCreateAgent())
    {
    }

    private static ChatClientAgent GetOrCreateAgent()
    {
        if (s_agent != null)
        {
            return s_agent;
        }

        if (s_initializationAttempted && s_initializationError != null)
        {
            throw s_initializationError;
        }

        s_initializationAttempted = true;

        try
        {
            // Try to get configuration from environment variables
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME")
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
                ?? Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
                ?? "gpt-4o-mini";
            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

            // If no Azure OpenAI config is available, fail with clear message
            if (string.IsNullOrEmpty(endpoint))
            {
                var errorMessage = @"
⚠️  Azure OpenAI Weather Agent requires configuration!

Please set these environment variables:
  • AZURE_OPENAI_ENDPOINT (required) - e.g., https://your-resource.openai.azure.com/
  • AZURE_OPENAI_DEPLOYMENT_NAME (optional) - defaults to 'gpt-4o-mini'
  • AZURE_OPENAI_API_KEY (optional) - if not set, will use DefaultAzureCredential

Example:
  export AZURE_OPENAI_ENDPOINT=""https://your-resource.openai.azure.com/""
  export AZURE_OPENAI_DEPLOYMENT_NAME=""gpt-4o-mini""
  export AZURE_OPENAI_API_KEY=""your-key""

Or use Azure AD authentication (no API key needed):
  az login
  export AZURE_OPENAI_ENDPOINT=""https://your-resource.openai.azure.com/""
";
                Console.WriteLine(errorMessage);
                s_initializationError = new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is not set");
                throw s_initializationError;
            }

            // Create Azure OpenAI client
            var azureClient = string.IsNullOrEmpty(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));

            // Create weather tools with explicit snake_case names for OpenAI compatibility
            // Function names must match pattern ^[a-zA-Z0-9_-]+$ per OpenAI requirements
            var weatherTools = new List<AITool>
            {
                AIFunctionFactory.Create(WeatherTools.GetWeather, name: "get_weather"),
                AIFunctionFactory.Create(WeatherTools.GetForecast, name: "get_forecast")
            };

            // Create the agent using the CreateAIAgent extension method with tools
            s_agent = azureClient
                .GetChatClient(deploymentName)
                .CreateAIAgent(
                    instructions: @"You are a helpful weather assistant. You have access to weather tools:
- get_weather: Gets current weather for a location
- get_forecast: Gets multi-day forecast for a location

When users ask about weather:
1. Use the appropriate tool to get weather information
2. Present the information in a friendly, conversational way
3. If asked about Atlantis, refuse and say it's a special place we don't check weather for!
4. Be helpful and provide relevant recommendations based on the weather

Always use the tools to get accurate weather data instead of making up information.",
                    name: "Azure Weather Agent",
                    description: "A helpful agent that provides weather information and forecasts using AI tools",
                    tools: weatherTools);

            Console.WriteLine($"✓ Weather Agent initialized with Azure OpenAI ({deploymentName}) and {weatherTools.Count} tools");
            return s_agent;
        }
        catch (Exception ex)
        {
            s_initializationError = ex;
            Console.WriteLine($"⚠️  Failed to initialize Azure OpenAI Weather Agent: {ex.Message}");
            throw;
        }
    }
}

/// <summary>
/// Weather tool functions for the agent
/// </summary>
public static class WeatherTools
{
    /// <summary>
    /// Get the weather for a given location
    /// </summary>
    [Description("Get the current weather for a given location")]
    public static string GetWeather(
        [Description("The location to get the weather for")] string location)
    {
        // Check for Atlantis (fun easter egg from Python version)
        if (location.Equals("atlantis", StringComparison.OrdinalIgnoreCase))
        {
            return "Atlantis is a special place, we must never ask about the weather there!!";
        }

        // Simulate weather data
        var conditions = new[] { "sunny", "cloudy", "rainy", "stormy" };
        var temperature = 53;
        var condition = conditions[0]; // Always sunny for now

        return $"The weather in {location} is {condition} with a high of {temperature}°C.";
    }

    /// <summary>
    /// Get weather forecast for multiple days
    /// </summary>
    [Description("Get weather forecast for multiple days")]
    public static string GetForecast(
        [Description("The location to get the forecast for")] string location,
        [Description("Number of days for forecast")] int days = 3)
    {
        // Check for Atlantis
        if (location.Equals("atlantis", StringComparison.OrdinalIgnoreCase))
        {
            return "Atlantis is a special place, we must never ask about the weather there!!";
        }

        // Simulate forecast data
        var conditions = new[] { "sunny", "cloudy", "rainy", "stormy" };
        var forecast = new List<string>();

        for (int day = 1; day <= days; day++)
        {
            var condition = conditions[0]; // Always sunny for now
            var temp = 53;
            forecast.Add($"Day {day}: {condition}, {temp}°C");
        }

        return $"Weather forecast for {location}:\n" + string.Join("\n", forecast);
    }
}
