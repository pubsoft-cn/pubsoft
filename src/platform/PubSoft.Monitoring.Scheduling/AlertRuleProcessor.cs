using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Monitoring;
using PubSoft.Notifications;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;

public sealed class AlertRuleProcessor : IResultProcessor
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxJobLocks = 4_096;
    private readonly IStateStore _stateStore;
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProbeHealthCheck _probeHealthCheck;
    private readonly AlertProcessorOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ILogger<AlertRuleProcessor>? _logger;
    /// <summary>job 级互斥锁表——按 jobKey 哈希键控；上限封顶 + 空闲项惰性清理，防止按 jobName 无界增长。</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _jobLocks = new(StringComparer.Ordinal);

    public AlertRuleProcessor(
        IStateStore stateStore,
        NotificationDispatcher dispatcher,
        IProbeHealthCheck probeHealthCheck,
        IOptions<AlertProcessorOptions> options,
        TimeProvider? timeProvider = null,
        ILogger<AlertRuleProcessor>? logger = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _probeHealthCheck = probeHealthCheck ?? throw new ArgumentNullException(nameof(probeHealthCheck));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (!_options.IsValid())
            throw new OptionsValidationException(
                nameof(AlertProcessorOptions),
                typeof(AlertProcessorOptions),
                ["FailureThreshold must be positive."]); // pubsoft:exempt PUBSOFT025 "single-use validation failure message — cold path"
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public string Name => "AlertRules";

    public async Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Name);

        var jobKey = GetStableKey(job.Name);
        var jobLock = GetJobLock(jobKey);
        await jobLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var probeHealth = await _probeHealthCheck.CheckAsync(cancellationToken).ConfigureAwait(false);
            var probeKey = GetStateKey(jobKey, "probe-unreachable");
            var wasProbeUnreachable = await GetStateAsync<bool>(probeKey, cancellationToken).ConfigureAwait(false) ?? false;
            if (probeHealth == ProbeHealth.Unreachable)
            {
                await SetStateAsync(probeKey, true, cancellationToken).ConfigureAwait(false);
                _logger?.LogWarning("Monitoring probe is unreachable; scheduler alerts are suppressed.");
                return;
            }

            var stateKey = GetStateKey(jobKey, "health-state");
            var failuresKey = GetStateKey(jobKey, "consecutive-failures");
            var fromState = await GetStateAsync<JobHealthState>(stateKey, cancellationToken).ConfigureAwait(false)
                ?? JobHealthState.Healthy;
            var failures = result.Success
                ? 0
                : (await GetStateAsync<int>(failuresKey, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
            var toState = TransitionState(fromState, result.Success, failures, _options.FailureThreshold);

            await SetStateAsync(failuresKey, failures, cancellationToken).ConfigureAwait(false);
            if (fromState != toState)
            {
                await SetStateAsync(stateKey, toState, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Scheduled job health state changed from {FromState} to {ToState}.", fromState, toState);
            }

            if (wasProbeUnreachable)
                await _stateStore.RemoveCustomStateAsync(probeKey, cancellationToken).ConfigureAwait(false);

            if (!TryReadAlertRules(job.ResultProcessorConfig, out var config))
            {
                _logger?.LogWarning("Alert rule configuration is invalid; no alerts will be dispatched.");
                return;
            }

            var context = new AlertContext
            {
                Job = job,
                Current = result,
                FromState = fromState,
                ToState = toState,
                ProbeHealth = probeHealth,
                WasProbeUnreachable = wasProbeUnreachable
            };

            foreach (var rule in config.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsInhibited(rule, context) || !EvaluateCondition(rule, context))
                    continue;

                var throttleKey = GetStateKey(jobKey, $"throttle:{GetStableKey(rule.Name)}");
                if (await IsThrottledAsync(throttleKey, rule.Throttle, cancellationToken).ConfigureAwait(false))
                    continue;

                var message = BuildMessage(rule, context);
                var delivered = false;
                foreach (var channel in rule.Channels)
                {
                    var dispatchResult = await _dispatcher.DispatchAsync(channel, message, cancellationToken)
                        .ConfigureAwait(false);
                    delivered |= dispatchResult.Success;
                }

                if (delivered && rule.Throttle is { } throttle && throttle > TimeSpan.Zero)
                    await SetStateAsync(throttleKey, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            jobLock.Release();
        }
    }

    internal static JobHealthState TransitionState(JobHealthState current, bool success, int failures, int threshold)
        => success ? JobHealthState.Healthy
            : current == JobHealthState.Unhealthy ? JobHealthState.Unhealthy
            : failures >= threshold ? JobHealthState.Unhealthy
            : JobHealthState.Suspicious;

    internal static bool IsInhibited(AlertRule rule, AlertContext context)
        => context.WasProbeUnreachable
            && rule.Condition is AlertConditionType.StateRecovered or AlertConditionType.Recovered
            || context.ProbeHealth == ProbeHealth.Unreachable
                && rule.Condition is AlertConditionType.FirstFailure or AlertConditionType.EveryFailure;

    internal static bool EvaluateCondition(AlertRule rule, AlertContext context)
        => rule.Condition switch
        {
            AlertConditionType.StateDegraded => context.FromState == JobHealthState.Healthy
                && context.ToState == JobHealthState.Suspicious,
            AlertConditionType.StateConfirmed => context.FromState != JobHealthState.Unhealthy
                && context.ToState == JobHealthState.Unhealthy,
            AlertConditionType.StateRecovered => context.FromState == JobHealthState.Unhealthy
                && context.ToState == JobHealthState.Healthy,
            AlertConditionType.SlowResponse => context.Current.Success
                && context.Current.DelayMs > (rule.DelayThresholdMs ?? 2000),
            AlertConditionType.FirstFailure => context.FromState == JobHealthState.Healthy && !context.Current.Success,
            AlertConditionType.Recovered => context.FromState != JobHealthState.Healthy && context.Current.Success,
            AlertConditionType.EveryFailure => !context.Current.Success,
            _ => false
        };

    private static NotificationMessage BuildMessage(AlertRule rule, AlertContext context)
    {
        var severity = rule.Severity switch
        {
            AlertSeverity.Critical => NotificationSeverity.Critical,
            AlertSeverity.Warning => NotificationSeverity.Warning,
            _ => NotificationSeverity.Info
        };
        var title = rule.Condition switch
        {
            AlertConditionType.StateConfirmed => $"[CRITICAL] {context.Job.Name} failure confirmed",
            AlertConditionType.StateRecovered or AlertConditionType.Recovered => $"[RECOVERY] {context.Job.Name} recovered",
            AlertConditionType.StateDegraded => $"[WARNING] {context.Job.Name} degraded",
            AlertConditionType.SlowResponse => $"[WARNING] {context.Job.Name} slow response",
            AlertConditionType.FirstFailure => $"[WARNING] {context.Job.Name} first failure",
            AlertConditionType.EveryFailure => $"[WARNING] {context.Job.Name} failed",
            _ => $"[ALERT] {context.Job.Name}"
        };
        var content = context.Current.Success
            ? $"State: {context.FromState} -> {context.ToState}; status: {context.Current.StatusCode}; delay: {context.Current.DelayMs} ms."
            : $"State: {context.FromState} -> {context.ToState}; status: {context.Current.StatusCode}; job execution failed.";
        var correlationId = $"monitoring:{GetStableKey(context.Job.Name)}:{GetStableKey(rule.Name)}:{context.Current.ExecutedAt.UtcTicks}";
        return new NotificationMessage(
            content,
            title: title,
            severity: severity,
            source: "monitoring",
            correlationId: correlationId);
    }

    private static bool TryReadAlertRules(JsonElement element, out AlertRuleConfig config)
    {
        config = new AlertRuleConfig();
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return true;

        try
        {
            config = element.Deserialize(AlertJsonContext.Default.AlertRuleConfig)!;
        }
        catch (JsonException)
        {
            return false;
        }

        if (config?.Rules is null || config.Rules.Count > 100)
            return false;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return config.Rules.All(rule => IsValidRule(rule, names));
    }

    private static bool IsValidRule(AlertRule? rule, HashSet<string> names)
        => rule is not null
            && !string.IsNullOrWhiteSpace(rule.Name)
            && names.Add(rule.Name)
            && Enum.IsDefined(rule.Condition)
            && Enum.IsDefined(rule.Severity)
            && rule.Channels is { Length: <= 20 }
            && rule.Channels.All(channel => !string.IsNullOrWhiteSpace(channel))
            && (rule.Throttle is null || rule.Throttle >= TimeSpan.Zero)
            && rule.DelayThresholdMs is not < 0;

    private static string GetStableKey(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string GetStateKey(string jobKey, string stateName)
        => $"monitoring-alert:v1:{jobKey}:{stateName}";

    private async Task<bool> IsThrottledAsync(string key, TimeSpan? throttle, CancellationToken cancellationToken)
    {
        if (throttle is null || throttle <= TimeSpan.Zero)
            return false;
        var lastSent = await GetStateAsync<DateTimeOffset>(key, cancellationToken).ConfigureAwait(false);
        return lastSent is { } timestamp && _timeProvider.GetUtcNow() - timestamp < throttle;
    }

    private async Task<T?> GetStateAsync<T>(string key, CancellationToken cancellationToken) where T : struct
    {
        var value = await _stateStore.GetCustomStateAsync<StateValue<T>>(key, cancellationToken).ConfigureAwait(false);
        return value?.Value;
    }

    private Task SetStateAsync<T>(string key, T value, CancellationToken cancellationToken) where T : struct
        => _stateStore.SetCustomStateAsync(key, new StateValue<T>(value), null, cancellationToken);

    /// <summary>获取/新建 job 锁——满载时先清理未持有的空闲信号量，仍满载则临时锁（不阻塞流程，仅并发隔离失效）。</summary>
    private SemaphoreSlim GetJobLock(string jobKey)
    {
        if (_jobLocks.TryGetValue(jobKey, out var existing))
            return existing;
        if (_jobLocks.Count >= MaxJobLocks)
            EvictIdleJobLocks();
        return _jobLocks.GetOrAdd(jobKey, static _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>清理未占用（CurrentCount==1）的 idle 锁条目——持锁中的条目跳过。</summary>
    private void EvictIdleJobLocks()
    {
        foreach (var pair in _jobLocks)
            if (pair.Value.CurrentCount == 1)
                _jobLocks.TryRemove(pair);
    }

    private sealed record StateValue<T>(T Value) where T : struct;
}