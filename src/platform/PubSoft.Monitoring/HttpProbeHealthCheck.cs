using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Monitoring;

/// <summary>Checks a bounded list of HTTP targets and aggregates their success ratio.</summary>
public sealed class HttpProbeHealthCheck : IProbeHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri[] _targets;
    private readonly double _minimumSuccessRatio;
    private readonly TimeSpan _probeTimeout;
    private readonly ILogger<HttpProbeHealthCheck>? _logger;

    /// <summary>Creates the probe from monitoring options; invalid options fail closed.</summary>
    public HttpProbeHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<MonitoringOptions> options,
        ILogger<HttpProbeHealthCheck>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        if (!value.IsValid(out var error))
            throw new OptionsValidationException(nameof(MonitoringOptions), typeof(MonitoringOptions), [error]);

        _httpClientFactory = httpClientFactory;
        _targets = value.ProbeTargets.Select(target => new Uri(target, UriKind.Absolute)).ToArray();
        _minimumSuccessRatio = value.MinProbeSuccessRatio;
        _probeTimeout = value.ProbeTimeout;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProbeHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_targets.Length == 0)
            return ProbeHealth.Healthy;

        var httpClient = _httpClientFactory.CreateClient(MonitoringOptions.HttpClientName);
        var outcomes = await Task.WhenAll(_targets
            .Select(target => ProbeTargetAsync(httpClient, target, cancellationToken)))
            .ConfigureAwait(false);
        var successfulTargets = outcomes.Count(outcome => outcome);

        var successRatio = (double)successfulTargets / _targets.Length;
        var health = successRatio >= _minimumSuccessRatio ? ProbeHealth.Healthy : ProbeHealth.Unreachable;
        if (health == ProbeHealth.Unreachable)
            _logger?.LogWarning("Monitoring probe health is unreachable: {Successful}/{Total} targets succeeded.",
                successfulTargets, _targets.Length);

        return health;
    }

    private async Task<bool> ProbeTargetAsync(HttpClient httpClient, Uri target, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_probeTimeout);

        try
        {
            using var response = await httpClient.GetAsync(
                target,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Monitoring probe target timed out.");
            return false;
        }
        catch (HttpRequestException)
        {
            _logger?.LogDebug("Monitoring probe target request failed.");
            return false;
        }
    }
}