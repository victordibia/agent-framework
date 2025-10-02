using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace Microsoft.Agents.AI.DevUI.Tests;

/// <summary>
/// Message Capture Script - Debug message flow
/// This script provides a reference for the types of events
/// emitted by the server when agents and workflows are executed
/// Equivalent to Python's capture_messages.py for output comparison
/// </summary>
public class CaptureMessages
{
    private const string ServerUrl = "http://127.0.0.1:8093";
    private const int ServerPort = 8093;
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task MainAsync(string[] args)
    {
        Console.WriteLine("🧪 .NET DevUI Message Capture Test");
        Console.WriteLine("===================================\n");

        // Setup output directory
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tests", "captured_messages");
        Directory.CreateDirectory(outputDir);

        // Start or verify server
        var serverProcess = await StartServerAsync();

        try
        {
            // Wait for server to be ready
            await WaitForServerAsync();

            // Capture all messages
            var results = await CaptureAllEntitiesAsync();

            // Save results to JSON (matching Python format)
            var outputFile = Path.Combine(outputDir, "dotnet_entities_stream_events.json");
            var output = new
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                server_type = "DotNetDevUI",
                runtime = ".NET 9.0",
                entities_tested = results
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(output, jsonOptions);
            await File.WriteAllTextAsync(outputFile, json);

            Console.WriteLine($"\n✅ Results saved to: {outputFile}");

            // Also create a comparison file
            await CreateComparisonReportAsync(outputDir);
        }
        finally
        {
            // Cleanup
            if (serverProcess != null && !serverProcess.HasExited)
            {
                Console.WriteLine("\n🛑 Stopping server...");
                serverProcess.Kill(true);
                serverProcess.Dispose();
            }
        }
    }

    private static async Task<Process?> StartServerAsync()
    {
        Console.WriteLine("🚀 Starting .NET DevUI server...");

        // Check if server is already running
        try
        {
            var health = await httpClient.GetAsync($"{ServerUrl}/health");
            if (health.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ Server already running");
                return null;
            }
        }
        catch
        {
            // Server not running, start it
        }

        // Start the server with samples
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run -p:TreatWarningsAsErrors=false -- --port {ServerPort} --entities-dir samples",
            WorkingDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..")),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
            throw new Exception("Failed to start server process");

        Console.WriteLine($"✓ Server process started (PID: {process.Id})");
        return process;
    }

    private static async Task WaitForServerAsync()
    {
        Console.WriteLine("⏳ Waiting for server to be ready...");

        for (int i = 0; i < 30; i++)
        {
            try
            {
                var response = await httpClient.GetAsync($"{ServerUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    var healthJson = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"✓ Server ready: {healthJson}");
                    return;
                }
            }
            catch
            {
                // Server not ready yet
            }

            await Task.Delay(1000);
            Console.Write(".");
        }

        throw new Exception("Server failed to start after 30 seconds");
    }

    private static async Task<Dictionary<string, object>> CaptureAllEntitiesAsync()
    {
        Console.WriteLine("\n📋 Discovering entities...");

        // Get all entities
        var response = await httpClient.GetAsync($"{ServerUrl}/v1/entities");
        var json = await response.Content.ReadAsStringAsync();
        var entitiesResponse = JsonSerializer.Deserialize<EntitiesResponse>(json);

        if (entitiesResponse?.Entities == null || entitiesResponse.Entities.Count == 0)
        {
            Console.WriteLine("⚠️ No entities found!");
            return new Dictionary<string, object>();
        }

        Console.WriteLine($"✓ Found {entitiesResponse.Entities.Count} entities");

        var results = new Dictionary<string, object>();

        foreach (var entity in entitiesResponse.Entities)
        {
            Console.WriteLine($"\n🔍 Testing {entity.Type}: {entity.Name} ({entity.Id})");

            var events = entity.Type switch
            {
                "agent" => await CaptureAgentEventsAsync(entity),
                "workflow" => await CaptureWorkflowEventsAsync(entity),
                _ => new List<object>()
            };

            var key = $"{entity.Type}_{entity.Id}";
            results[key] = new
            {
                entity_info = entity,
                events = events
            };

            Console.WriteLine($"   ✓ Captured {events.Count} events");
        }

        return results;
    }

    private static async Task<List<object>> CaptureAgentEventsAsync(EntityInfo entity)
    {
        var events = new List<object>();

        try
        {
            // Test non-streaming first
            Console.WriteLine("   📨 Testing non-streaming...");
            var nonStreamingRequest = new
            {
                model = entity.Id,
                messages = new[]
                {
                    new { role = "user", content = "Tell me about the weather in Tokyo. I want details." }
                },
                extra_body = new { entity_id = entity.Id }
            };

            var nonStreamingResponse = await PostJsonAsync("/v1/responses", nonStreamingRequest);
            events.Add(new { type = "non_streaming", response = nonStreamingResponse });

            // Test streaming
            Console.WriteLine("   📡 Testing streaming...");
            var streamingRequest = new
            {
                model = entity.Id,
                messages = new[]
                {
                    new { role = "user", content = "Tell me about the weather in Tokyo. I want details." }
                },
                extra_body = new { entity_id = entity.Id },
                stream = true
            };

            var streamingEvents = await CaptureStreamingEventsAsync("/v1/responses", streamingRequest);
            foreach (var evt in streamingEvents)
            {
                events.Add(new { type = "streaming", @event = evt });
            }
        }
        catch (Exception ex)
        {
            events.Add(new
            {
                type = "error",
                error_message = ex.Message,
                error_type = ex.GetType().Name,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        return events;
    }

    private static async Task<List<object>> CaptureWorkflowEventsAsync(EntityInfo entity)
    {
        var events = new List<object>();

        try
        {
            // Test workflow execution
            Console.WriteLine("   ⚙️ Testing workflow execution...");
            var request = new
            {
                model = entity.Id,
                messages = new[]
                {
                    new {
                        role = "user",
                        content = "Process this spam detection workflow with multiple emails: 'Buy now!', 'Hello mom', 'URGENT: Click here!'"
                    }
                },
                extra_body = new { entity_id = entity.Id }
            };

            var response = await PostJsonAsync("/v1/responses", request);
            events.Add(new { type = "workflow_execution", response = response });

            // Test streaming if supported
            var streamingRequest = new
            {
                model = entity.Id,
                messages = request.messages,
                extra_body = request.extra_body,
                stream = true
            };

            var streamingEvents = await CaptureStreamingEventsAsync("/v1/responses", streamingRequest);
            foreach (var evt in streamingEvents)
            {
                events.Add(new { type = "workflow_streaming", @event = evt });
            }
        }
        catch (Exception ex)
        {
            events.Add(new
            {
                type = "error",
                error_message = ex.Message,
                error_type = ex.GetType().Name,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                entity_type = "workflow"
            });
        }

        return events;
    }

    private static async Task<object> PostJsonAsync(string endpoint, object request)
    {
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{ServerUrl}{endpoint}", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<object>(responseJson) ?? new { };
    }

    private static async Task<List<object>> CaptureStreamingEventsAsync(string endpoint, object request)
    {
        var events = new List<object>();
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await httpClient.PostAsync($"{ServerUrl}{endpoint}", content);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        int eventCount = 0;
        while ((line = await reader.ReadLineAsync()) != null && eventCount < 200)
        {
            if (line.StartsWith("data: "))
            {
                var eventData = line.Substring(6);
                if (eventData == "[DONE]") break;

                try
                {
                    var evt = JsonSerializer.Deserialize<object>(eventData);
                    if (evt != null)
                    {
                        events.Add(evt);
                        eventCount++;
                    }
                }
                catch
                {
                    // Skip malformed events
                }
            }
        }

        return events;
    }

    private static async Task CreateComparisonReportAsync(string outputDir)
    {
        Console.WriteLine("\n📊 Creating comparison report...");

        var dotnetFile = Path.Combine(outputDir, "dotnet_entities_stream_events.json");
        var pythonFile = Path.Combine(outputDir, "entities_stream_events.json");

        if (!File.Exists(pythonFile))
        {
            Console.WriteLine("⚠️ Python results not found. Run Python capture_messages.py first for comparison.");
            return;
        }

        // Create simple comparison report
        var report = new StringBuilder();
        report.AppendLine("# .NET vs Python Output Comparison");
        report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n");

        try
        {
            var dotnetJson = await File.ReadAllTextAsync(dotnetFile);
            var pythonJson = await File.ReadAllTextAsync(pythonFile);

            var dotnetData = JsonSerializer.Deserialize<Dictionary<string, object>>(dotnetJson);
            var pythonData = JsonSerializer.Deserialize<Dictionary<string, object>>(pythonJson);

            report.AppendLine("## File Sizes");
            report.AppendLine($"- .NET output: {new FileInfo(dotnetFile).Length:N0} bytes");
            report.AppendLine($"- Python output: {new FileInfo(pythonFile).Length:N0} bytes");
            report.AppendLine();

            report.AppendLine("## Structure Comparison");
            report.AppendLine($"- .NET entities tested: {(dotnetData?.ContainsKey("entities_tested") ?? false)}");
            report.AppendLine($"- Python entities tested: {(pythonData?.ContainsKey("entities_tested") ?? false)}");
            report.AppendLine();

            report.AppendLine("## Notes");
            report.AppendLine("- Both outputs should have similar structure");
            report.AppendLine("- Event formats should match OpenAI specifications");
            report.AppendLine("- Entity discovery should find the same agents/workflows");
            report.AppendLine();
            report.AppendLine("For detailed comparison, use a JSON diff tool on the two files.");
        }
        catch (Exception ex)
        {
            report.AppendLine($"Error creating comparison: {ex.Message}");
        }

        var reportFile = Path.Combine(outputDir, "comparison_report.md");
        await File.WriteAllTextAsync(reportFile, report.ToString());
        Console.WriteLine($"✓ Comparison report saved to: {reportFile}");
    }

    // DTOs for JSON deserialization
    private class EntitiesResponse
    {
        [JsonPropertyName("entities")]
        public List<EntityInfo> Entities { get; set; } = new();
    }

    private class EntityInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}