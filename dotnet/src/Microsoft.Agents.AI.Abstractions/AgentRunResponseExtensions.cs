﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides extension methods for working with <see cref="AgentRunResponse"/> and <see cref="AgentRunResponseUpdate"/> instances.
/// </summary>
public static class AgentRunResponseExtensions
{
    /// <summary>
    /// Creates a <see cref="ChatResponse"/> from an <see cref="AgentRunResponse"/> instance.
    /// </summary>
    /// <param name="response">The <see cref="AgentRunResponse"/> to convert.</param>
    /// <returns>A <see cref="ChatResponse"/> built from the specified <paramref name="response"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If the <paramref name="response"/>'s <see cref="AgentRunResponse.RawRepresentation"/> is already a
    /// <see cref="ChatResponse"/> instance, that instance is returned directly.
    /// Otherwise, a new <see cref="ChatResponse"/> is created and populated with the data from the <paramref name="response"/>.
    /// The resulting instance is a shallow copy; any reference-type members (e.g. <see cref="AgentRunResponse.Messages"/>)
    /// will be shared between the two instances.
    /// </remarks>
    public static ChatResponse AsChatResponse(this AgentRunResponse response)
    {
        Throw.IfNull(response);

        return
            response.RawRepresentation as ChatResponse ??
            new()
            {
                AdditionalProperties = response.AdditionalProperties,
                CreatedAt = response.CreatedAt,
                Messages = response.Messages,
                RawRepresentation = response,
                ResponseId = response.ResponseId,
                Usage = response.Usage,
            };
    }

    /// <summary>
    /// Creates a <see cref="ChatResponseUpdate"/> from an <see cref="AgentRunResponseUpdate"/> instance.
    /// </summary>
    /// <param name="responseUpdate">The <see cref="AgentRunResponseUpdate"/> to convert.</param>
    /// <returns>A <see cref="ChatResponseUpdate"/> built from the specified <paramref name="responseUpdate"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="responseUpdate"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// If the <paramref name="responseUpdate"/>'s <see cref="AgentRunResponseUpdate.RawRepresentation"/> is already a
    /// <see cref="ChatResponseUpdate"/> instance, that instance is returned directly.
    /// Otherwise, a new <see cref="ChatResponseUpdate"/> is created and populated with the data from the <paramref name="responseUpdate"/>.
    /// The resulting instance is a shallow copy; any reference-type members (e.g. <see cref="AgentRunResponseUpdate.Contents"/>)
    /// will be shared between the two instances.
    /// </remarks>
    public static ChatResponseUpdate AsChatResponseUpdate(this AgentRunResponseUpdate responseUpdate)
    {
        Throw.IfNull(responseUpdate);

        return
            responseUpdate.RawRepresentation as ChatResponseUpdate ??
            new()
            {
                AdditionalProperties = responseUpdate.AdditionalProperties,
                AuthorName = responseUpdate.AuthorName,
                Contents = responseUpdate.Contents,
                CreatedAt = responseUpdate.CreatedAt,
                MessageId = responseUpdate.MessageId,
                RawRepresentation = responseUpdate,
                ResponseId = responseUpdate.ResponseId,
                Role = responseUpdate.Role,
            };
    }

    /// <summary>
    /// Creates an asynchronous enumerable of <see cref="ChatResponseUpdate"/> instances from an asynchronous
    /// enumerable of <see cref="AgentRunResponseUpdate"/> instances.
    /// </summary>
    /// <param name="responseUpdates">The sequence of <see cref="AgentRunResponseUpdate"/> instances to convert.</param>
    /// <returns>An asynchronous enumerable of <see cref="ChatResponseUpdate"/> instances built from <paramref name="responseUpdates"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="responseUpdates"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Each <see cref="AgentRunResponseUpdate"/> is converted to a <see cref="ChatResponseUpdate"/> using
    /// <see cref="AsChatResponseUpdate"/>.
    /// </remarks>
    public static async IAsyncEnumerable<ChatResponseUpdate> AsChatResponseUpdatesAsync(
        this IAsyncEnumerable<AgentRunResponseUpdate> responseUpdates)
    {
        Throw.IfNull(responseUpdates);

        await foreach (var responseUpdate in responseUpdates.ConfigureAwait(false))
        {
            yield return responseUpdate.AsChatResponseUpdate();
        }
    }

    /// <summary>
    /// Combines a sequence of <see cref="AgentRunResponseUpdate"/> instances into a single <see cref="AgentRunResponse"/>.
    /// </summary>
    /// <param name="updates">The sequence of updates to be combined into a single response.</param>
    /// <returns>A single <see cref="AgentRunResponse"/> that represents the combined state of all the updates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updates"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// As part of combining <paramref name="updates"/> into a single <see cref="AgentRunResponse"/>, the method will attempt to reconstruct
    /// <see cref="ChatMessage"/> instances. This includes using <see cref="AgentRunResponseUpdate.MessageId"/> to determine
    /// message boundaries, as well as coalescing contiguous <see cref="AIContent"/> items where applicable, e.g. multiple
    /// <see cref="TextContent"/> instances in a row may be combined into a single <see cref="TextContent"/>.
    /// </remarks>
    public static AgentRunResponse ToAgentRunResponse(
        this IEnumerable<AgentRunResponseUpdate> updates)
    {
        _ = Throw.IfNull(updates);

        AgentRunResponse response = new();

        foreach (var update in updates)
        {
            ProcessUpdate(update, response);
        }

        FinalizeResponse(response);

        return response;
    }

    /// <summary>
    /// Asynchronously combines a sequence of <see cref="AgentRunResponseUpdate"/> instances into a single <see cref="AgentRunResponse"/>.
    /// </summary>
    /// <param name="updates">The asynchronous sequence of updates to be combined into a single response.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a single <see cref="AgentRunResponse"/> that represents the combined state of all the updates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updates"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This is the asynchronous version of <see cref="ToAgentRunResponse(IEnumerable{AgentRunResponseUpdate})"/>.
    /// It performs the same combining logic but operates on an asynchronous enumerable of updates.
    /// </para>
    /// <para>
    /// As part of combining <paramref name="updates"/> into a single <see cref="AgentRunResponse"/>, the method will attempt to reconstruct
    /// <see cref="ChatMessage"/> instances. This includes using <see cref="AgentRunResponseUpdate.MessageId"/> to determine
    /// message boundaries, as well as coalescing contiguous <see cref="AIContent"/> items where applicable, e.g. multiple
    /// <see cref="TextContent"/> instances in a row may be combined into a single <see cref="TextContent"/>.
    /// </para>
    /// </remarks>
    public static Task<AgentRunResponse> ToAgentRunResponseAsync(
        this IAsyncEnumerable<AgentRunResponseUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(updates);

        return ToAgentRunResponseAsync(updates, cancellationToken);

        static async Task<AgentRunResponse> ToAgentRunResponseAsync(
            IAsyncEnumerable<AgentRunResponseUpdate> updates,
            CancellationToken cancellationToken)
        {
            AgentRunResponse response = new();

            await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                ProcessUpdate(update, response);
            }

            FinalizeResponse(response);

            return response;
        }
    }

    /// <summary>Coalesces sequential <see cref="AIContent"/> content elements.</summary>
    internal static void CoalesceTextContent(List<AIContent> contents)
    {
        Coalesce<TextContent>(contents, static text => new(text));
        Coalesce<TextReasoningContent>(contents, static text => new(text));

        // This implementation relies on TContent's ToString returning its exact text.
        static void Coalesce<TContent>(List<AIContent> contents, Func<string, TContent> fromText)
            where TContent : AIContent
        {
            StringBuilder? coalescedText = null;

            // Iterate through all of the items in the list looking for contiguous items that can be coalesced.
            int start = 0;
            while (start < contents.Count - 1)
            {
                // We need at least two TextContents in a row to be able to coalesce.
                if (contents[start] is not TContent firstText)
                {
                    start++;
                    continue;
                }

                if (contents[start + 1] is not TContent secondText)
                {
                    start += 2;
                    continue;
                }

                // Append the text from those nodes and continue appending subsequent TextContents until we run out.
                // We null out nodes as their text is appended so that we can later remove them all in one O(N) operation.
                coalescedText ??= new();
                _ = coalescedText.Clear().Append(firstText).Append(secondText);
                contents[start + 1] = null!;
                int i = start + 2;
                for (; i < contents.Count && contents[i] is TContent next; i++)
                {
                    _ = coalescedText.Append(next);
                    contents[i] = null!;
                }

                // Store the replacement node. We inherit the properties of the first text node. We don't
                // currently propagate additional properties from the subsequent nodes. If we ever need to,
                // we can add that here.
                var newContent = fromText(coalescedText.ToString());
                contents[start] = newContent;
                newContent.AdditionalProperties = firstText.AdditionalProperties?.Clone();

                start = i;
            }

            // Remove all of the null slots left over from the coalescing process.
            _ = contents.RemoveAll(u => u is null);
        }
    }

    /// <summary>Finalizes the <paramref name="response"/> object.</summary>
    private static void FinalizeResponse(AgentRunResponse response)
    {
        int count = response.Messages.Count;
        for (int i = 0; i < count; i++)
        {
            CoalesceTextContent((List<AIContent>)response.Messages[i].Contents);
        }
    }

    /// <summary>Processes the <see cref="AgentRunResponseUpdate"/>, incorporating its contents into <paramref name="response"/>.</summary>
    /// <param name="update">The update to process.</param>
    /// <param name="response">The <see cref="AgentRunResponse"/> object that should be updated based on <paramref name="update"/>.</param>
    private static void ProcessUpdate(AgentRunResponseUpdate update, AgentRunResponse response)
    {
        // If there is no message created yet, or if the last update we saw had a different
        // message ID or role than the newest update, create a new message.
        ChatMessage message;
        var isNewMessage = false;
        if (response.Messages.Count == 0)
        {
            isNewMessage = true;
        }
        else if (update.MessageId is { Length: > 0 } updateMessageId
            && response.Messages[response.Messages.Count - 1].MessageId is string lastMessageId
            && updateMessageId != lastMessageId)
        {
            isNewMessage = true;
        }
        else if (update.Role is { } updateRole
            && response.Messages[response.Messages.Count - 1].Role is { } lastRole
            && updateRole != lastRole)
        {
            isNewMessage = true;
        }

        if (isNewMessage)
        {
            message = new(ChatRole.Assistant, []);
            response.Messages.Add(message);
        }
        else
        {
            message = response.Messages[response.Messages.Count - 1];
        }

        // Some members on AgentRunResponseUpdate map to members of ChatMessage.
        // Incorporate those into the latest message; in cases where the message
        // stores a single value, prefer the latest update's value over anything
        // stored in the message.
        if (update.AuthorName is not null)
        {
            message.AuthorName = update.AuthorName;
        }

        if (message.CreatedAt is null || (update.CreatedAt is not null && update.CreatedAt > message.CreatedAt))
        {
            message.CreatedAt = update.CreatedAt;
        }

        if (update.Role is ChatRole role)
        {
            message.Role = role;
        }

        if (update.MessageId is { Length: > 0 })
        {
            // Note that this must come after the message checks earlier, as they depend
            // on this value for change detection.
            message.MessageId = update.MessageId;
        }

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                // Usage content is treated specially and propagated to the response's Usage.
                case UsageContent usage:
                    (response.Usage ??= new()).Add(usage.Details);
                    break;

                default:
                    message.Contents.Add(content);
                    break;
            }
        }

        // Other members on a AgentRunResponseUpdate map to members of the AgentRunResponse.
        // Update the response object with those, preferring the values from later updates.

        if (update.AgentId is { Length: > 0 })
        {
            response.AgentId = update.AgentId;
        }

        if (update.ResponseId is { Length: > 0 })
        {
            response.ResponseId = update.ResponseId;
        }

        if (response.CreatedAt is null || (update.CreatedAt is not null && update.CreatedAt > response.CreatedAt))
        {
            response.CreatedAt = update.CreatedAt;
        }

        if (update.AdditionalProperties is not null)
        {
            if (response.AdditionalProperties is null)
            {
                response.AdditionalProperties = new(update.AdditionalProperties);
            }
            else
            {
                foreach (var item in update.AdditionalProperties)
                {
                    response.AdditionalProperties[item.Key] = item.Value;
                }
            }
        }
    }
}
