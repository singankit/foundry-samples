// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace EchoAgent;

/// <summary>
/// Stores user.id keyed by W3C TraceId so that the OTel span processor can stamp
/// it on every span in the trace — including infrastructure spans (HttpRequestOut,
/// DefaultAzureCredential.GetToken) that run on thread-pool threads where
/// Baggage.Current is not reliably propagated.
/// </summary>
internal sealed class TraceUserIdStore
{
    private readonly ConcurrentDictionary<ActivityTraceId, string> _store = new();

    /// <summary>Records the user.id for the given trace.</summary>
    public void Set(ActivityTraceId traceId, string userId) =>
        _store[traceId] = userId;

    /// <summary>Returns the user.id for the given trace, or null if not found.</summary>
    public string? Get(ActivityTraceId traceId) =>
        _store.TryGetValue(traceId, out var userId) ? userId : null;

    /// <summary>Removes the entry when the root span ends to avoid memory leaks.</summary>
    public void Remove(ActivityTraceId traceId) =>
        _store.TryRemove(traceId, out _);
}
