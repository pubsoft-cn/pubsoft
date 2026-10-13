using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Scheduling;
using PubSoft.Tokens;

namespace PubSoft.Scheduling.Http;

public sealed class ProbeExecutor : IJobExecutor
{
    private readonly HttpExecutionSupport _http;
    private readonly ILogger<ProbeExecutor>? _logger;

    public ProbeExecutor(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpExecutorOptions> options,
        ITokenProvider? tokenProvider = null,
        ILogger<ProbeExecutor>? logger = null)
    {
        _http = new HttpExecutionSupport(httpClientFactory, options, tokenProvider);
        _logger = logger;
    }

    public string Type => "Http.Probe";

    public async Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        if (!_http.TryReadConfig(job, out var config, out var uri, out var method))
            return new JobResult { Success = false, ErrorMessage = "Invalid HttpExecutorConfig" };

        var attempt = await _http.SendAsync(config, uri, method, cancellationToken).ConfigureAwait(false);
        if (!attempt.Success && attempt.StatusCode != 0)
            _logger?.LogDebug("HTTP probe failed with status {StatusCode}.", attempt.StatusCode);
        return attempt.ToJobResult();
    }
}