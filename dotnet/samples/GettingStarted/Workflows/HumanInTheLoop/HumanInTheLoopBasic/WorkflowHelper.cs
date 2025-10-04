﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace WorkflowHumanInTheLoopBasicSample;

internal static class WorkflowHelper
{
    /// <summary>
    /// Get a workflow that plays a number guessing game with human-in-the-loop interaction.
    /// An input port allows the external world to provide inputs to the workflow upon requests.
    /// </summary>
    internal static ValueTask<Workflow<NumberSignal>> GetWorkflowAsync()
    {
        // Create the executors
        RequestPort numberRequestPort = RequestPort.Create<NumberSignal, int>("GuessNumber");
        JudgeExecutor judgeExecutor = new(42);

        // Build the workflow by connecting executors in a loop
        return new WorkflowBuilder(numberRequestPort)
            .AddEdge(numberRequestPort, judgeExecutor)
            .AddEdge(judgeExecutor, numberRequestPort)
            .WithOutputFrom(judgeExecutor)
            .BuildAsync<NumberSignal>();
    }
}

/// <summary>
/// Signals used for communication between guesses and the JudgeExecutor.
/// </summary>
internal enum NumberSignal
{
    Init,
    Above,
    Below,
}

/// <summary>
/// Executor that judges the guess and provides feedback.
/// </summary>
internal sealed class JudgeExecutor() : ReflectingExecutor<JudgeExecutor>("Judge"), IMessageHandler<int>
{
    private readonly int _targetNumber;
    private int _tries;

    /// <summary>
    /// Initializes a new instance of the <see cref="JudgeExecutor"/> class.
    /// </summary>
    /// <param name="targetNumber">The number to be guessed.</param>
    public JudgeExecutor(int targetNumber) : this()
    {
        this._targetNumber = targetNumber;
    }

    public async ValueTask HandleAsync(int message, IWorkflowContext context)
    {
        this._tries++;
        if (message == this._targetNumber)
        {
            await context.YieldOutputAsync($"{this._targetNumber} found in {this._tries} tries!")
                         .ConfigureAwait(false);
        }
        else if (message < this._targetNumber)
        {
            await context.SendMessageAsync(NumberSignal.Below).ConfigureAwait(false);
        }
        else
        {
            await context.SendMessageAsync(NumberSignal.Above).ConfigureAwait(false);
        }
    }
}
