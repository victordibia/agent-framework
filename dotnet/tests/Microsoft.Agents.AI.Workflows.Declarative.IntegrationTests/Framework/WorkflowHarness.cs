﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows.Declarative.Events;
using Shared.Code;

namespace Microsoft.Agents.AI.Workflows.Declarative.IntegrationTests.Framework;

internal sealed class WorkflowHarness(Workflow workflow, string runId)
{
    private readonly CheckpointManager _checkpointManager = CheckpointManager.CreateInMemory();
    private CheckpointInfo? LastCheckpoint { get; set; }

    public async Task<WorkflowEvents> RunTestcaseAsync<TInput>(Testcase testcase, TInput input) where TInput : notnull
    {
        WorkflowEvents workflowEvents = await this.RunAsync(input);
        int requestCount = (workflowEvents.InputEvents.Count + 1) / 2;
        int responseCount = 0;
        while (requestCount > responseCount)
        {
            Assert.NotNull(testcase.Setup.Responses);
            Assert.NotEmpty(testcase.Setup.Responses);
            string inputText = testcase.Setup.Responses[responseCount].Value;
            Console.WriteLine($"INPUT: {inputText}");
            InputResponse response = new(inputText);
            ++responseCount;
            WorkflowEvents runEvents = await this.ResumeAsync(response).ConfigureAwait(false);
            workflowEvents = new WorkflowEvents([.. workflowEvents.Events, .. runEvents.Events]);
            requestCount = (workflowEvents.InputEvents.Count + 1) / 2;
        }

        return workflowEvents;
    }

    private async Task<WorkflowEvents> RunAsync<TInput>(TInput input) where TInput : notnull
    {
        Console.WriteLine("RUNNING WORKFLOW...");
        Checkpointed<StreamingRun> run = await InProcessExecution.StreamAsync(workflow, input, this._checkpointManager, runId);
        IReadOnlyList<WorkflowEvent> workflowEvents = await this.MonitorWorkflowRunAsync(run).ToArrayAsync();
        this.LastCheckpoint = workflowEvents.OfType<SuperStepCompletedEvent>().LastOrDefault()?.CompletionInfo?.Checkpoint;
        return new WorkflowEvents(workflowEvents);
    }

    private async Task<WorkflowEvents> ResumeAsync(InputResponse response)
    {
        Console.WriteLine("RESUMING WORKFLOW...");
        Assert.NotNull(this.LastCheckpoint);
        Checkpointed<StreamingRun> run = await InProcessExecution.ResumeStreamAsync(workflow, this.LastCheckpoint, this._checkpointManager, runId);
        IReadOnlyList<WorkflowEvent> workflowEvents = await this.MonitorWorkflowRunAsync(run, response).ToArrayAsync();
        return new WorkflowEvents(workflowEvents);
    }

    public static async Task<WorkflowHarness> GenerateCodeAsync<TInput>(
        string runId,
        string workflowProviderCode,
        string workflowProviderName,
        string workflowProviderNamespace,
        DeclarativeWorkflowOptions options,
        TInput input) where TInput : notnull
    {
        // Compile the code
        Assembly assembly = Compiler.Build(workflowProviderCode, Compiler.RepoDependencies(typeof(DeclarativeWorkflowBuilder)));
        Type? type = assembly.GetType($"{workflowProviderNamespace}.{workflowProviderName}");
        Assert.NotNull(type);
        MethodInfo? method = type.GetMethod("CreateWorkflow");
        Assert.NotNull(method);
        MethodInfo genericMethod = method.MakeGenericMethod(typeof(TInput));
        object? workflowObject = genericMethod.Invoke(null, [options, null]);
        Workflow workflow = Assert.IsType<Workflow>(workflowObject);

        return new WorkflowHarness(workflow, runId);
    }

    private async IAsyncEnumerable<WorkflowEvent> MonitorWorkflowRunAsync(Checkpointed<StreamingRun> run, InputResponse? response = null)
    {
        await foreach (WorkflowEvent workflowEvent in run.Run.WatchStreamAsync().ConfigureAwait(false))
        {
            bool exitLoop = false;

            switch (workflowEvent)
            {
                case RequestInfoEvent requestInfo:
                    Console.WriteLine($"REQUEST #{requestInfo.Request.RequestId}");
                    if (response is not null)
                    {
                        ExternalResponse requestResponse = requestInfo.Request.CreateResponse(response);
                        await run.Run.SendResponseAsync(requestResponse).ConfigureAwait(false);
                        response = null;
                    }
                    else
                    {
                        await run.Run.EndRunAsync().ConfigureAwait(false);
                        exitLoop = true;
                    }
                    break;
                case DeclarativeActionInvokedEvent actionInvokeEvent:
                    Console.WriteLine($"ACTION: {actionInvokeEvent.ActionId} [{actionInvokeEvent.ActionType}]");
                    break;
            }

            yield return workflowEvent;

            if (exitLoop)
            {
                break;
            }
        }

        Console.WriteLine("SUSPENDING WORKFLOW...");
    }
}
