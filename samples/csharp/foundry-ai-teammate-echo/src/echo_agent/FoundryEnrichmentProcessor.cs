// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace EchoAgent;

/// <summary>
/// An OpenTelemetry span processor that enriches every span with Foundry
/// agent identity and project attributes (session, conversation, agent name/version/id).
/// </summary>
internal sealed class FoundryEnrichmentProcessor : BaseProcessor<Activity>
{
    private readonly ILogger<FoundryEnrichmentProcessor> _logger;
    private readonly TraceUserIdStore _userIdStore;
    private readonly string? _agentName;
    private readonly string? _agentVersion;
    private readonly string? _agentId;
    private readonly string? _projectId;
    private readonly string? _blueprintId;
    private readonly string? _tenantId;
    private readonly string? _agentType;
    private readonly string? _sessionId;

    public FoundryEnrichmentProcessor(ILogger<FoundryEnrichmentProcessor> logger, TraceUserIdStore userIdStore)
    {
        _logger = logger;
        _userIdStore = userIdStore;
        _agentName = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME");
        _agentVersion = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_VERSION");
        _projectId = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ARM_ID");
        _blueprintId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_BLUEPRINT_CLIENT_ID");
        _tenantId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_TENANT_ID");

        // Determine if hosted (session ID env var presence is a good signal)
        var sessionId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_SESSION_ID");
        _sessionId = sessionId;
        _agentType = !string.IsNullOrEmpty(sessionId) ? "hosted" : null;

        // Agent ID resolution: prefer instance client ID (managed identity),
        // fall back to name:version or just name.
        var instanceClientId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_INSTANCE_CLIENT_ID");
        if (!string.IsNullOrEmpty(instanceClientId))
        {
            _agentId = instanceClientId;
        }
        else
        {
            _agentId = _agentName is not null && _agentVersion is not null
                ? $"{_agentName}:{_agentVersion}"
                : _agentName;
        }
    }

    /// <inheritdoc/>
    public override void OnStart(Activity activity)
    {
        if (_projectId is not null)
        {
            activity.SetTag("microsoft.foundry.project.id", _projectId);
        }

        // Stamp all incoming baggage entries as span tags on the root HTTP span.
        foreach (var entry in activity.Baggage)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value))
            {
                _logger.LogInformation("[FoundryEnrichment] Baggage: {Key} = {Value}", entry.Key, entry.Value);
                activity.SetTag($"baggage.{entry.Key}", entry.Value);
            }
        }

        var sessionId = Baggage.Current.GetBaggage("azure.ai.agentserver.session_id");
        if (!string.IsNullOrWhiteSpace(sessionId))
            activity.SetTag("microsoft.session.id", sessionId);

        var conversationId = Baggage.Current.GetBaggage("azure.ai.agentserver.conversation_id");
        if (!string.IsNullOrWhiteSpace(conversationId))
            activity.SetTag("gen_ai.conversation.id", conversationId);

        var userId = _userIdStore.Get(activity.TraceId)
                     ?? Baggage.Current.GetBaggage("user.id");
        _logger.LogInformation("[FoundryEnrichment][OnStart] span='{SpanName}' TraceId={TraceId} userId='{UserId}'",
            activity.DisplayName, activity.TraceId, userId ?? "<null>");
        if (!string.IsNullOrWhiteSpace(userId))
            activity.SetTag("user.id", userId);
    }

    /// <inheritdoc/>
    public override void OnEnd(Activity activity)
    {
        // Stamp user.id in OnEnd to cover spans whose OnStart fired before the store
        // was populated (e.g. HttpRequestIn starts before the /api/messages handler runs).
        // For root spans, read before removing from the store.
        var userId = _userIdStore.Get(activity.TraceId)
                     ?? Baggage.Current.GetBaggage("user.id");
        _logger.LogInformation("[FoundryEnrichment][OnEnd] span='{SpanName}' TraceId={TraceId} userId='{UserId}'",
            activity.DisplayName, activity.TraceId, userId ?? "<null>");
        if (!string.IsNullOrWhiteSpace(userId))
            activity.SetTag("user.id", userId);

        // Clean up TraceUserIdStore for root spans to prevent memory leaks.
        if (activity.Parent is null)
        {
            _userIdStore.Remove(activity.TraceId);
        }

        if (_agentName is not null)
        {
            activity.SetTag("gen_ai.agent.name", _agentName);
        }

        if (_agentVersion is not null)
        {
            activity.SetTag("gen_ai.agent.version", _agentVersion);
        }

        if (_agentId is not null)
        {
            activity.SetTag("gen_ai.agent.id", _agentId);
        }

        if (!string.IsNullOrEmpty(_blueprintId))
        {
            activity.SetTag("microsoft.a365.agent.blueprint.id", _blueprintId);
        }

        if (!string.IsNullOrEmpty(_tenantId))
        {
            activity.SetTag("microsoft.tenant.id", _tenantId);
        }

        if (_agentType is not null)
        {
            activity.SetTag("microsoft.foundry.agent.type", _agentType);
        }

        if (!string.IsNullOrEmpty(_sessionId))
        {
            activity.SetTag("microsoft.session.id", _sessionId);
        }
    }
}
