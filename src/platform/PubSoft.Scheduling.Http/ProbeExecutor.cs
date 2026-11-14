using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Tokens;

namespace PubSoft.Scheduling.Http;

/// <summary>Simple HTTP probe executor.</summary>
public sealed class ProbeExecutor : IJobExecutor
{
    private readonly HttpExecutionSupport _http;
    private readonly ILogger<ProbeExecutor>? _logger;

    /// <summary>Creates the executor.</summary>
    public ProbeExecutor(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpExecutorOptions> options,
        ITokenProvider? tokenProvider = null,
        ILogger<ProbeExecutor>? logger = null)
    {
        _http = new HttpExecutionSupport(httpClientFactory, options, tokenProvider);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Type => "Http.Probe";

    /// <inheritdoc />
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