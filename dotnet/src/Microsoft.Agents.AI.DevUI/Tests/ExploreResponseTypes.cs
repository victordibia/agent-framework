using System.Reflection;
using OpenAI;

namespace Microsoft.Agents.AI.DevUI.Tests;

/// <summary>
/// Explores available OpenAI Response types in the .NET OpenAI package
/// </summary>
public static class ExploreResponseTypes
{
    public static async Task MainAsync(string[] args)
    {
        Console.WriteLine("🔍 Exploring OpenAI Response Types in .NET");
        Console.WriteLine("==========================================\n");

        // Get OpenAI assembly
        var openaiAssembly = typeof(OpenAIClient).Assembly;
        Console.WriteLine($"📦 Assembly: {openaiAssembly.GetName().Name} v{openaiAssembly.GetName().Version}");
        Console.WriteLine($"📍 Location: {openaiAssembly.Location}");
        Console.WriteLine();

        // Find all types containing "Response"
        var responseTypes = openaiAssembly.GetTypes()
            .Where(t => t.FullName != null &&
                       (t.FullName.Contains("Response") ||
                        t.FullName.Contains("response") ||
                        t.Name.Contains("Response") ||
                        t.Name.Contains("Delta") ||
                        t.Name.Contains("Event")))
            .OrderBy(t => t.FullName)
            .ToArray();

        Console.WriteLine($"🔍 Found {responseTypes.Length} Response-related types:");
        Console.WriteLine();

        foreach (var type in responseTypes)
        {
            Console.WriteLine($"📋 {type.FullName}");

            // Show if it has streaming-related properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.Contains("Delta") ||
                           p.Name.Contains("Type") ||
                           p.Name.Contains("Event") ||
                           p.Name.Contains("Content"))
                .Take(3);

            foreach (var prop in properties)
            {
                Console.WriteLine($"   • {prop.Name}: {prop.PropertyType.Name}");
            }

            Console.WriteLine();
        }

        // Look specifically for streaming event types
        Console.WriteLine("🌊 Looking for Streaming Event Types:");
        Console.WriteLine("====================================");

        var streamingTypes = responseTypes
            .Where(t => t.Name.Contains("Delta") ||
                       t.Name.Contains("Event") ||
                       t.Name.Contains("Stream"))
            .ToArray();

        foreach (var type in streamingTypes)
        {
            Console.WriteLine($"🔥 {type.FullName}");

            // Show all public properties
            var allProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in allProps.Take(5))
            {
                Console.WriteLine($"   • {prop.Name}: {prop.PropertyType.Name}");
            }
            Console.WriteLine();
        }

        // Look for namespaces containing "responses"
        Console.WriteLine("📦 Namespaces containing 'responses':");
        Console.WriteLine("====================================");

        var responseNamespaces = openaiAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.ToLower().Contains("response"))
            .Select(t => t.Namespace!)
            .Distinct()
            .OrderBy(ns => ns);

        foreach (var ns in responseNamespaces)
        {
            Console.WriteLine($"📁 {ns}");

            var typesInNs = openaiAssembly.GetTypes()
                .Where(t => t.Namespace == ns)
                .Select(t => t.Name)
                .OrderBy(n => n)
                .Take(5);

            foreach (var typeName in typesInNs)
            {
                Console.WriteLine($"   • {typeName}");
            }
            Console.WriteLine();
        }
    }
}