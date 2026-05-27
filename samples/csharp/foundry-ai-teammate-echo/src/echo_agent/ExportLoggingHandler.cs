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

        return await base.SendAsync(request, cancellationToken);
    }
}
