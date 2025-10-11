using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Microsoft.Agents.AI.DevUI.Samples;

/// <summary>
/// Weather agent powered by Azure OpenAI Chat Completion
/// This agent provides weather information using AI
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

            // Create the agent using the CreateAIAgent extension method
            s_agent = azureClient
                .GetChatClient(deploymentName)
                .CreateAIAgent(
                    instructions: @"You are a helpful weather assistant. When users ask about weather:

1. If they provide a location, give detailed weather information including:
   - Current conditions (temperature, sky conditions, wind)
   - Short-term forecast
   - Helpful recommendations based on the weather

2. If no location is provided, politely ask for their location

3. Be conversational and friendly

Note: You don't have access to real-time weather data, so provide plausible weather information based on:
- Typical patterns for that geographic location
- Current season and time of year
- General climate knowledge

Always acknowledge that you're providing estimated information, not real-time data.",
                    name: "Weather Agent",
                    description: "Provides weather information for locations using Azure OpenAI");

            Console.WriteLine($"✓ Weather Agent initialized with Azure OpenAI ({deploymentName})");
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
