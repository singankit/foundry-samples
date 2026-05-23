using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace EchoAgent;

public class EchoAgentApplication : AgentApplication
{
    private readonly AIAgent _aiAgent;
    private static readonly ActivitySource _activitySource = new(TelemetrySourceName);

    public const string TelemetrySourceName = "EchoAgent.AI";

    public EchoAgentApplication(AgentApplicationOptions options, ILogger<EchoAgentApplication> logger)
        : base(options)
    {

        // Create the AI agent using Microsoft Agent Framework + Foundry
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
            ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
        var modelDeployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
            ?? "gpt-chat-latest";
        var agentName = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_NAME") ?? "echo-agent";
        var agentVersion = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_VERSION");
        var agentId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_INSTANCE_CLIENT_ID");
        var sessionId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_SESSION_ID");
        var a365TracingEnabled = Environment.GetEnvironmentVariable("FOUNDRY_AGENT365_TRACING_ENABLED");

        logger.LogInformation("Initializing agent: {Name} (version: {Version}, id: {Id}, sessionId: {SessionId}, a365Tracing: {A365Tracing})", agentName, agentVersion, agentId, sessionId, a365TracingEnabled);

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = agentId,
        });

        _aiAgent = new AIProjectClient(new Uri(endpoint), credential)
            .AsAIAgent(
                model: modelDeployment,
                instructions: "You are a helpful assistant. Respond concisely to user messages.",
                name: agentName,
                clientFactory: inner => inner
                    .AsBuilder()
                    .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true)
                    .Build())
            .AsBuilder()
            .UseOpenTelemetry(sourceName: TelemetrySourceName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();

        // Handle all message activities — invoke LLM with user message
        OnActivity(ActivityTypes.Message, async (turnContext, turnState, cancellationToken) =>
        {
            var userMessage = turnContext.Activity.Text ?? "(empty)";
            var userName = turnContext.Activity.From?.Name ?? "Unknown";

            logger.LogInformation("Received message from {User}: {Message}", userName, userMessage);

            // Restore the trace context from the incoming HTTP request so that
            // the invoke_agent span (created by UseOpenTelemetry on AIAgent) becomes
            // a child of the platform's request span in App Insights.
            var parentContext = RequestContextHolder.LastContext;
            System.Diagnostics.Activity? processTurn = null;
            if (parentContext.HasValue)
            {
                processTurn = _activitySource.StartActivity(
                    "process_turn",
                    ActivityKind.Internal,
                    parentContext.Value);
            }

            try
            {
                // Call the LLM via the agent framework
                var llmResponse = await _aiAgent.RunAsync(userMessage, cancellationToken: cancellationToken);
                var responseText = llmResponse.ToString();

                logger.LogInformation("LLM response: {Response}", responseText);

                await turnContext.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
            }
            finally
            {
                processTurn?.Stop();
                processTurn?.Dispose();
            }
        });

        // Handle install/uninstall
        OnActivity(ActivityTypes.InstallationUpdate, async (turnContext, turnState, cancellationToken) =>
        {
            var action = turnContext.Activity.Action;
            if (string.Equals(action, "add", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Agent installed");
                await turnContext.SendActivityAsync(MessageFactory.Text("Hello! I'm the Echo Agent powered by AI. Ask me anything!"), cancellationToken);
            }
            else
            {
                logger.LogInformation("Agent removed");
            }
        });
    }
}
