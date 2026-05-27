using Azure.Identity;
using EchoAgent;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.Http;
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IStorage, MemoryStorage>();
builder.AddAgentApplicationOptions();
builder.AddAgent<EchoAgentApplication>();

// Register the export logging handler to intercept A365 exporter HTTP calls
builder.Services.AddTransient<ExportLoggingHandler>();
builder.Services.ConfigureAll<HttpClientFactoryOptions>(options =>
{
    options.HttpMessageHandlerBuilderActions.Add(b =>
        b.AdditionalHandlers.Add(b.Services.GetRequiredService<ExportLoggingHandler>()));
});

// Telemetry configuration
var appInsightsConnStr = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
var a365Enabled = string.Equals(Environment.GetEnvironmentVariable("FOUNDRY_AGENT365_TRACING_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
var agentClientId = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_INSTANCE_CLIENT_ID");

if (!string.IsNullOrEmpty(appInsightsConnStr) || a365Enabled)
{
    var exportTargets = ExportTarget.None;
    if (!string.IsNullOrEmpty(appInsightsConnStr))
        exportTargets |= ExportTarget.AzureMonitor;
    if (a365Enabled)
        exportTargets |= ExportTarget.Agent365;

    builder.Services.AddOpenTelemetry()
        .UseMicrosoftOpenTelemetry(o =>
        {
            o.Exporters = exportTargets;

            // Azure Monitor connection string (explicit for logs export)
            if (!string.IsNullOrEmpty(appInsightsConnStr))
            {
                o.AzureMonitor.ConnectionString = appInsightsConnStr;
            }

            // Agent365 exporter config
            if (a365Enabled)
            {
                o.Agent365.Exporter.UseS2SEndpoint = true;
                o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
                {
                    try
                    {
                        Console.WriteLine($"[A365] Acquiring token. agentId={agentId} tenantId={tenantId} miClientId={agentClientId ?? "<none>"}");
                        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                        {
                            ManagedIdentityClientId = agentClientId
                        });
                        var token = await credential.GetTokenAsync(
                            new Azure.Core.TokenRequestContext(new[] { "api://9b975845-388f-4429-889e-eab1ef63949c/.default" }));
                        Console.WriteLine($"[A365] Token acquired, expiresOn={token.ExpiresOn:o}");
                        return token.Token;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[A365] Token acquisition FAILED: {ex.GetType().Name}: {ex.Message}");
                        throw;
                    }
                };
            }
        })
        .WithTracing(tracing =>
        {
            // Disable sampling so all requests (and their logs) are exported
            tracing.SetSampler(new AlwaysOnSampler());
            tracing.AddProcessor(new FoundryEnrichmentProcessor());
            tracing.AddConsoleExporter();
            tracing.AddSource(
                EchoAgentApplication.TelemetrySourceName,
                "EchoAgent",
                "Microsoft.OpenTelemetry",
                "Microsoft.Agents.Builder",
                "Microsoft.Agents.Hosting",
                "Microsoft.Agents",
                "Microsoft.Agents.A365",
                "Microsoft.AspNetCore");
        })
        .WithLogging();

    // Configure OpenTelemetry logger options so formatted messages and scopes
    // are captured in exported log records.
    builder.Services.Configure<OpenTelemetryLoggerOptions>(o =>
    {
        o.IncludeFormattedMessage = true;
        o.IncludeScopes = true;
        o.ParseStateValues = true;
    });

    // Bridge ILogger<T> into the OpenTelemetry log pipeline so that logs
    // from OnActivity handlers are exported to Azure Monitor.
    builder.Logging.AddOpenTelemetry(otelLogging =>
    {
        otelLogging.IncludeFormattedMessage = true;
        otelLogging.IncludeScopes = true;
        otelLogging.ParseStateValues = true;
    });
}

// Capture Debug-level logs (and above) so they appear on console and reach
// telemetry exporters regardless of any Logging:LogLevel overrides.
// (Console provider is already registered by WebApplication.CreateBuilder.)
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Azure.AI.AgentServer", LogLevel.Debug);
builder.Logging.AddFilter("OpenTelemetry", LogLevel.Debug);
builder.Logging.AddFilter("Microsoft.OpenTelemetry", LogLevel.Debug);

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Echo Agent starting...");

// Print all environment variables for debugging
logger.LogInformation("=== Environment Variables ===");
foreach (var envVar in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().OrderBy(e => e.Key.ToString()))
{
    logger.LogInformation("  {Key} = {Value}", envVar.Key, envVar.Value);
}
logger.LogInformation("=== End Environment Variables ===");

app.UseRouting();
app.Use(next => context =>
{
    context.Request.EnableBuffering();
    return next(context);
});

app.MapPost("/api/messages", async (HttpContext httpContext, HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    request.EnableBuffering();

    // Log all incoming request headers
    logger.LogInformation("[/api/messages] === Request Headers ===");
    foreach (var header in request.Headers)
    {
        logger.LogInformation("[/api/messages] {Key}: {Value}", header.Key, header.Value.ToString());
    }
    logger.LogInformation("[/api/messages] === End Headers ===");

    var current = System.Diagnostics.Activity.Current;
    if (current != null)
    {
        // Add user.id as baggage so it propagates to all child spans (including MAF spans)
        current.AddBaggage("user.id", "fd50db2c-2ab7-415d-8f1d-2e66a7c71e54");

        logger.LogInformation("[/api/messages] Activity.Current: {DisplayName}, TraceId={TraceId}, SpanId={SpanId}",
            current.DisplayName, current.TraceId, current.SpanId);
        foreach (var bag in current.Baggage)
        {
            logger.LogInformation("[/api/messages] Baggage: {Key}={Value}", bag.Key, bag.Value);
        }
        EchoAgent.RequestContextHolder.LastContext = current.Context;
    }
    else
    {
        logger.LogWarning("[/api/messages] Activity.Current is NULL");
    }

    await adapter.ProcessAsync(request, response, agent, cancellationToken);
});

app.MapGet("/", () => "Echo Agent is running!");
app.MapGet("/liveness", () => "OK");
app.MapGet("/readiness", () => "OK");

app.Run();
