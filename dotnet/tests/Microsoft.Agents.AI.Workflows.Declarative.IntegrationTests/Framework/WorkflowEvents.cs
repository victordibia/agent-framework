﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Agents.AI.Workflows.Declarative.IntegrationTests.Framework;

internal sealed class WorkflowEvents
{
    public WorkflowEvents(IReadOnlyList<WorkflowEvent> workflowEvents)
    {
        this.Events = workflowEvents;
        this.EventCounts = workflowEvents.GroupBy(e => e.GetType()).ToDictionary(e => e.Key, e => e.Count());
        this.ActionInvokeEvents = workflowEvents.OfType<DeclarativeActionInvokedEvent>().ToList();
        this.ActionCompleteEvents = workflowEvents.OfType<DeclarativeActionCompletedEvent>().ToList();
        this.ConversationEvents = workflowEvents.OfType<ConversationUpdateEvent>().ToList();
        this.ExecutorInvokeEvents = workflowEvents.OfType<ExecutorInvokedEvent>().ToList();
        this.ExecutorCompleteEvents = workflowEvents.OfType<ExecutorCompletedEvent>().ToList();
        this.InputEvents = workflowEvents.OfType<RequestInfoEvent>().ToList();
    }

    public IReadOnlyList<WorkflowEvent> Events { get; }
    public IReadOnlyDictionary<Type, int> EventCounts { get; }
    public IReadOnlyList<ConversationUpdateEvent> ConversationEvents { get; }
    public IReadOnlyList<DeclarativeActionInvokedEvent> ActionInvokeEvents { get; }
    public IReadOnlyList<DeclarativeActionCompletedEvent> ActionCompleteEvents { get; }
    public IReadOnlyList<ExecutorInvokedEvent> ExecutorInvokeEvents { get; }
    public IReadOnlyList<ExecutorCompletedEvent> ExecutorCompleteEvents { get; }
    public IReadOnlyList<RequestInfoEvent> InputEvents { get; }
}
