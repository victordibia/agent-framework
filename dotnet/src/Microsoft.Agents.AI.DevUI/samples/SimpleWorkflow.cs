using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace Microsoft.Agents.AI.DevUI.Samples;

/// <summary>
/// Simple workflow example that processes text through multiple executors
/// </summary>
public static class SimpleWorkflow
{
    public static async Task<Workflow<string>> CreateAsync()
    {
        // Create the executors
        var processExecutor = new ProcessTextExecutor();
        var finalizeExecutor = new FinalizeExecutor();

        // Build the workflow by connecting executors sequentially
        var builder = new WorkflowBuilder(processExecutor);
        builder.AddEdge(processExecutor, finalizeExecutor);

        return await builder.BuildAsync<string>();
    }
}

/// <summary>
/// First executor: processes input text
/// </summary>
internal sealed class ProcessTextExecutor() : ReflectingExecutor<ProcessTextExecutor>("ProcessTextExecutor"), IMessageHandler<string, string>
{
    public async ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken); // Simulate some processing time
        string result = $"Processed: {message.ToUpperInvariant()}";
        return result;
    }
}

/// <summary>
/// Final executor: finalizes the workflow
/// </summary>
internal sealed class FinalizeExecutor() : ReflectingExecutor<FinalizeExecutor>("FinalizeExecutor"), IMessageHandler<string, string>
{
    public async ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate finalization
        string result = $"[FINAL] {message} - Workflow Complete!";

        // Signal that the workflow is complete
        await context.AddEventAsync(new ExecutorCompletedEvent("FinalizeExecutor", result), cancellationToken).ConfigureAwait(false);

        return result;
    }
}