using System.Text;
using System.Text.Json;

namespace EchoAgent;

/// <summary>
/// A delegating handler that logs outgoing HTTP request bodies for A365 exporter calls.
/// </summary>
internal sealed class ExportLoggingHandler : DelegatingHandler
{
    private readonly ILogger<ExportLoggingHandler> _logger;

    public ExportLoggingHandler(ILogger<ExportLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";
        if (url.Contains("agent365.svc.cloud.microsoft", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[A365 Export] URL: {Method} {Uri}", request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                _logger.LogInformation("[A365 Export] Header: {Key}: {Value}", header.Key, string.Join(", ", header.Value));
            }
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                // Log in chunks of 80000 chars
                for (int i = 0; i < body.Length; i += 80000)
                {
                    var chunk = body.Substring(i, Math.Min(80000, body.Length - i));
                    _logger.LogInformation("[A365 Export] Body[{Offset}]: {Chunk}", i, chunk);
                }
            }
        }

        if (url.Contains("trafficmanager.net", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("botframework.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[Bot Connector] URL: {Method} {Uri}", request.Method, request.RequestUri);
            if (request.Headers.Authorization?.Scheme?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true)
            {
                var claims = TryExtractJwtClaims(request.Headers.Authorization.Parameter ?? string.Empty);
                if (claims is not null)
                {
                    _logger.LogInformation(
                        "[Bot Connector] Token claims appid={AppId} azp={Azp} tid={Tid} aud={Aud} scp={Scp} roles={Roles}",
                        claims.Value.AppId,
                        claims.Value.Azp,
                        claims.Value.Tid,
                        claims.Value.Aud,
                        claims.Value.Scp,
                        claims.Value.Roles);
                }
                else
                {
                    _logger.LogWarning("[Bot Connector] Could not decode bearer token claims.");
                }
            }
            else
            {
                _logger.LogWarning("[Bot Connector] Missing bearer Authorization header.");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static (string? AppId, string? Azp, string? Tid, string? Aud, string? Scp, string? Roles)? TryExtractJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return (
                root.TryGetProperty("appid", out var appid) ? appid.GetString() : null,
                root.TryGetProperty("azp", out var azp) ? azp.GetString() : null,
                root.TryGetProperty("tid", out var tid) ? tid.GetString() : null,
                root.TryGetProperty("aud", out var aud) ? aud.GetString() : null,
                root.TryGetProperty("scp", out var scp) ? scp.GetString() : null,
                root.TryGetProperty("roles", out var roles) ? roles.ToString() : null
            );
        }
        catch
        {
            return null;
        }
    }
}
