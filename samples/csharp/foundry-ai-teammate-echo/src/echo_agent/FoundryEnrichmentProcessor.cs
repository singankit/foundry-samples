// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using OpenTelemetry;

namespace EchoAgent;

/// <summary>
/// An OpenTelemetry span processor that enriches every span with Foundry
/// agent identity and project attributes (session, conversation, agent name/version/id).
/// </summary>
internal sealed class FoundryEnrichmentProcessor : BaseProcessor<Activity>
{
    private readonly string? _agentName;
    private readonly string? _agentVersion;
    private readonly string? _agentId;
    private readonly string? _projectId;
    private readonly string? _blueprintId;
    private readonly string? _tenantId;
    private readonly string? _agentType;
    private readonly string? _sessionId;

    public FoundryEnrichmentProcessor()
    {
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

        // Log and stamp ALL baggage entries as span tags
        var baggage = activity.Baggage;
        foreach (var entry in baggage)
        {
            if (!string.IsNullOrWhiteSpace(entry.Value))
            {
                Console.WriteLine($"[FoundryEnrichment] Baggage: {entry.Key} = {entry.Value}");
                activity.SetTag($"baggage.{entry.Key}", entry.Value);
            }
        }

        // Also set well-known semantic attributes from baggage
        var sessionId = activity.GetBaggageItem("azure.ai.agentserver.session_id");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            activity.SetTag("microsoft.session.id", sessionId);
        }

        var conversationId = activity.GetBaggageItem("azure.ai.agentserver.conversation_id");
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            activity.SetTag("gen_ai.conversation.id", conversationId);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Agent identity tags are set in OnEnd so they take precedence over
    /// any values an underlying framework may have stamped during the span's lifetime.
    /// </remarks>
    public override void OnEnd(Activity activity)
    {
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
