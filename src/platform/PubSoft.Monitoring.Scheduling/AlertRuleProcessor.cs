using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Notifications;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;

/// <summary>Result processor evaluating declared alert rules against job-run health transitions and dispatching to notification channels.</summary>
public sealed class AlertRuleProcessor : IResultProcessor
{
    /// <summary>Result-processor name jobs declare in <see cref="ScheduledJob.ResultProcessors"/> to opt into alert rules.</summary>
    public const string ProcessorName = "AlertRules";

    private const int MaxJobLocks = 4_096;
    private const string OutageStartKey = "monitoring-env:v1:outage-start";
    private const string EnvPendingJobKey = "monitoring-env";
    private readonly IStateStore _stateStore;
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProbeHealthCheck _probeHealthCheck;
    private readonly AlertProcessorOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ILogger<AlertRuleProcessor>? _logger;
    /// <summary>Per-job mutex table keyed by jobKey -- strict cap, never evicted (see <see cref="GetJobLock"/>).</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _jobLocks = new(StringComparer.Ordinal);
    /// <summary>Serializes outage-window bookkeeping across jobs: the outage-start key is global state, so concurrent jobs must not race its read-modify-write (which would multiply the immediate alert and shift the window start).</summary>
    private readonly SemaphoreSlim _envLock = new(1, 1);
    /// <summary>
    /// Serializes resend passes over the shared environment-alert backlog: concurrent jobs must not race its
    /// read-modify-write, or a retired entry is resurrected by a stale writer and a pass overlaps itself.
    /// Leaf lock -- never held together with <see cref="_envLock"/>; ordering is jobLock then envLock then envFlushLock.
    /// </summary>
    private readonly SemaphoreSlim _envFlushLock = new(1, 1);
    /// <summary>Executions suppressed during the current egress outage, observed by this instance only.</summary>
    private int _suppressedDuringOutage;

    /// <summary>Creates the processor; <paramref name="timeProvider"/> defaults to the system clock.</summary>
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
        if (!_options.IsValid(out var optionsError))
            throw new OptionsValidationException(
                nameof(AlertProcessorOptions),
                typeof(AlertProcessorOptions),
                [optionsError!]); // pubsoft:exempt PUBSOFT025 "single-use validation failure message - cold path"
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ProcessorName;

    /// <inheritdoc />
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
                await RecordOutageTickAsync(cancellationToken).ConfigureAwait(false);
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
            if (_options.PendingAlerts.Enabled)
            {
                await FlushPendingAlertsAsync(jobKey, cancellationToken).ConfigureAwait(false);
                await FlushEnvPendingAlertsAsync(cancellationToken).ConfigureAwait(false);
            }
            if (_options.Environment.Enabled)
                await ReportOutageRecoveryAsync(cancellationToken).ConfigureAwait(false);

            if (!TryReadAlertRules(job.ResultProcessorConfig, _logger, out var config, job.Name))
            {
                _logger?.LogWarning("Alert rule configuration for job {Job} is invalid; no alerts will be dispatched.", job.Name);
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
                if (IsInhibited(rule, context))
                {
                    if (EvaluateCondition(rule, context))
                        _logger?.LogInformation(
                            "Recovery alert rule {Rule} for job {Job} is inhibited on the first execution after probe restoration; the recovery edge is consumed, not replayed.",
                            rule.Name, job.Name);
                    continue;
                }
                if (!EvaluateCondition(rule, context))
                    continue;

                var throttleKey = GetStateKey(jobKey, $"throttle:{GetStableKey(rule.Name)}");
                if (await IsThrottledAsync(throttleKey, rule.Throttle, cancellationToken).ConfigureAwait(false))
                    continue;

                var message = BuildMessage(rule, context);
                var delivered = false;
                List<string>? failedChannels = null;
                foreach (var channel in rule.Channels)
                {
                    var dispatchResult = await _dispatcher.DispatchAsync(channel, message, cancellationToken)
                        .ConfigureAwait(false);
                    if (dispatchResult.Success)
                        delivered = true;
                    else if (_options.PendingAlerts.Enabled)
                        (failedChannels ??= []).Add(channel);
                }

                if (delivered && rule.Throttle is { } throttle && throttle > TimeSpan.Zero)
                    await SetStateAsync(throttleKey, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                if (failedChannels is { } failed)
                    await RecordPendingAlertsAsync(jobKey, message, failed, cancellationToken).ConfigureAwait(false);
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
            && rule.Condition is AlertConditionType.StateRecovered or AlertConditionType.Recovered;

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

    /// <summary>Validates and deserializes the alert rules of one job; also called by <see cref="AlertRuleLintHostedService"/> at startup. <paramref name="jobName"/> scopes the log messages when provided.</summary>
    internal static bool TryReadAlertRules(JsonElement element, ILogger? logger, out AlertRuleConfig config, string? jobName = null)
    {
        config = new AlertRuleConfig();
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return true;

        var jobFragment = jobName is null ? string.Empty : $" for job '{jobName}'";
        try
        {
            config = element.Deserialize(AlertJsonContext.Default.AlertRuleConfig)!;
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(exception, "Alert rule configuration{JobFragment} is not valid JSON: {Message}", jobFragment, exception.Message);
            LogUnparseableRules(element, logger, jobFragment);
            return false;
        }

        if (config?.Rules is null)
        {
            logger?.LogWarning("Alert rule configuration{JobFragment} has no 'Rules' array.", jobFragment);
            return false;
        }
        if (config.Rules.Count > 100)
        {
            logger?.LogWarning("Alert rule configuration{JobFragment} declares {Count} rules; the limit is 100.", jobFragment, config.Rules.Count);
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < config.Rules.Count; index++)
        {
            if (!IsValidRule(config.Rules[index], names, out var reason))
            {
                logger?.LogWarning("Alert rule {Index} ('{Name}'){JobFragment} is invalid: {Reason}",
                    index, config.Rules[index]?.Name, jobFragment, reason);
                return false;
            }
        }
        return true;
    }

    private static NotificationMessage BuildMessage(AlertRule rule, AlertContext context)
    {
        var severity = MapSeverity(rule.Severity);
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
        var templateParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["job"] = context.Job.Name,
            ["status"] = context.Current.StatusCode.ToString(CultureInfo.InvariantCulture),
            ["state"] = StateName(context.ToState),
            ["severity"] = SeverityName(rule.Severity)
        };
        if (rule.TemplateParameters is { } declared)
            foreach (var (key, value) in declared)
                templateParameters[key] = value;
        return new NotificationMessage(
            content,
            recipients: rule.Recipients,
            title: title,
            templateCode: rule.TemplateCode,
            templateParameters: templateParameters,
            severity: severity,
            source: "monitoring",
            correlationId: correlationId);
    }

    /// <summary>After a whole-config parse failure, retries each rule entry so the log names the offending indexes; bounded like the rule count cap.</summary>
    private static void LogUnparseableRules(JsonElement element, ILogger? logger, string jobFragment)
    {
        if (logger is null
            || !element.TryGetProperty("Rules", out var rules)
            || rules.ValueKind != JsonValueKind.Array)
            return;
        var index = 0;
        foreach (var ruleElement in rules.EnumerateArray())
        {
            if (index >= 100)
                break;
            try
            {
                ruleElement.Deserialize(AlertJsonContext.Default.AlertRule);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Alert rule {Index}{JobFragment} could not be deserialized: {Message}", index, jobFragment, exception.Message);
            }
            index++;
        }
    }

    private static bool IsValidRule(AlertRule? rule, HashSet<string> names, [NotNullWhen(false)] out string? reason)
    {
        reason = null;
        if (rule is null) { reason = "entry is null"; return false; }
        if (string.IsNullOrWhiteSpace(rule.Name)) { reason = "Name is required"; return false; }
        if (!names.Add(rule.Name)) { reason = "Name is duplicated"; return false; }
        if (!Enum.IsDefined(rule.Condition)) { reason = $"Condition '{rule.Condition}' is not a declared value"; return false; }
        if (!Enum.IsDefined(rule.Severity)) { reason = $"Severity '{rule.Severity}' is not a declared value"; return false; }
        if (rule.Channels is not { Length: >= 1 and <= 20 } || rule.Channels.Any(string.IsNullOrWhiteSpace))
        { reason = "Channels must hold 1-20 non-empty names"; return false; }
        if (rule.Recipients is { Length: > 50 } || rule.Recipients?.Any(string.IsNullOrWhiteSpace) == true)
        { reason = "Recipients must hold at most 50 non-empty entries"; return false; }
        if (rule.TemplateParameters is { Count: > 50 } || rule.TemplateParameters?.Keys.Any(string.IsNullOrWhiteSpace) == true)
        { reason = "TemplateParameters must hold at most 50 entries with non-empty keys"; return false; }
        if (rule.Throttle is { } throttle && throttle < TimeSpan.Zero) { reason = "Throttle must not be negative"; return false; }
        if (rule.DelayThresholdMs < 0) { reason = "DelayThresholdMs must not be negative"; return false; }
        return true;
    }

    private static NotificationSeverity MapSeverity(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => NotificationSeverity.Critical,
        AlertSeverity.Warning => NotificationSeverity.Warning,
        _ => NotificationSeverity.Info
    };

    private static string StateName(JobHealthState state) => state switch
    {
        JobHealthState.Healthy => nameof(JobHealthState.Healthy),
        JobHealthState.Suspicious => nameof(JobHealthState.Suspicious),
        JobHealthState.Unhealthy => nameof(JobHealthState.Unhealthy),
        _ => "Unknown"
    };

    private static string SeverityName(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Info => nameof(AlertSeverity.Info),
        AlertSeverity.Warning => nameof(AlertSeverity.Warning),
        AlertSeverity.Critical => nameof(AlertSeverity.Critical),
        _ => "Unknown"
    };

    /// <summary>Marks one suppressed execution and, on the first unreachable tick of an outage, persists the outage start and optionally attempts the immediate alert.</summary>
    private async Task RecordOutageTickAsync(CancellationToken cancellationToken)
    {
        if (_options.Environment is not { Enabled: true })
            return;
        Interlocked.Increment(ref _suppressedDuringOutage);
        await _envLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var start = await GetStateAsync<DateTimeOffset>(OutageStartKey, cancellationToken).ConfigureAwait(false);
            if (start is not null)
                return;
            var now = _timeProvider.GetUtcNow();
            await SetStateAsync(OutageStartKey, now, cancellationToken).ConfigureAwait(false);
            if (_options.Environment.SendImmediate)
                await DispatchEnvironmentAlertAsync(
                    $"Monitoring egress unreachable since {now:HH:mm} UTC; job alerts are suppressed until it recovers.",
                    now,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["windowStart"] = now.ToString("O", CultureInfo.InvariantCulture)
                    },
                    immediate: true,
                    cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _envLock.Release();
        }
    }

    /// <summary>On probe recovery, closes the persisted outage window and dispatches the delayed summary once per window.</summary>
    private async Task ReportOutageRecoveryAsync(CancellationToken cancellationToken)
    {
        await _envLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var start = await GetStateAsync<DateTimeOffset>(OutageStartKey, cancellationToken).ConfigureAwait(false);
            if (start is not { } started)
                return;
            await _stateStore.RemoveCustomStateAsync(OutageStartKey, cancellationToken).ConfigureAwait(false);
            var suppressed = Interlocked.Exchange(ref _suppressedDuringOutage, 0);
            var now = _timeProvider.GetUtcNow();
            var duration = now - started;
            if (duration < _options.Environment.MinUnreachableDuration)
            {
                _logger?.LogInformation(
                    "Monitoring egress outage lasted {Duration}; below the alert threshold of {Threshold}.",
                    duration, _options.Environment.MinUnreachableDuration);
                return;
            }
            await DispatchEnvironmentAlertAsync(
                $"Monitoring egress outage {started:HH:mm}-{now:HH:mm} UTC ({duration.TotalMinutes:F0} min); {suppressed} job executions suppressed on this instance.",
                started,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["windowStart"] = started.ToString("O", CultureInfo.InvariantCulture),
                    ["windowMinutes"] = duration.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture),
                    ["suppressedCount"] = suppressed.ToString(CultureInfo.InvariantCulture)
                },
                immediate: false,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _envLock.Release();
        }
    }

    /// <summary>
    /// Dispatches an environment alert; the correlation id derives from the outage window so duplicate detections collapse
    /// through dispatcher deduplication, and immediate/summary carries a distinct suffix so one cannot suppress the other.
    /// Failures of the recovery summary enter the pending backlog when enabled; immediate sends stay best-effort by design.
    /// </summary>
    private async Task DispatchEnvironmentAlertAsync(
        string content, DateTimeOffset outageStart, IReadOnlyDictionary<string, string> templateParameters, bool immediate, CancellationToken cancellationToken)
    {
        var message = new NotificationMessage(
            content,
            title: "[ENV] monitoring egress outage",
            templateParameters: templateParameters,
            severity: MapSeverity(_options.Environment.Severity),
            source: "monitoring-env",
            correlationId: $"monitoring-env:{outageStart.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}{(immediate ? ":immediate" : null)}");
        List<string>? failedChannels = null;
        foreach (var channel in _options.Environment.Channels)
        {
            var result = await _dispatcher.DispatchAsync(channel, message, cancellationToken).ConfigureAwait(false);
            if (result.Success)
                continue;
            _logger?.LogWarning(
                "Environment alert delivery to channel {Channel} failed (correlation {CorrelationId}): {Reason}",
                channel, message.CorrelationId, result.Message);
            if (!immediate && _options.PendingAlerts.Enabled)
                (failedChannels ??= []).Add(channel);
        }
        if (failedChannels is { } failed)
            await RecordPendingAlertsAsync(EnvPendingJobKey, message, failed, cancellationToken).ConfigureAwait(false);
    }

    private static string GetStableKey(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Flushes the shared environment-alert backlog at most once per <see cref="PendingAlertOptions.RetryInterval"/>.
    /// Unlike the per-job backlog (whose cadence is the owning job's own schedule), the environment backlog is
    /// global -- without throttling every job execution would retry it, multiplying the resend rate and, on a
    /// persistently failing channel, turning one undelivered summary into a poison-message loop.
    /// </summary>
    private async Task FlushEnvPendingAlertsAsync(CancellationToken cancellationToken)
    {
        await _envFlushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pendingKey = GetStateKey(EnvPendingJobKey, "pending-alerts");
            var backlog = await _stateStore.GetCustomStateAsync<List<PendingAlert>>(pendingKey, cancellationToken).ConfigureAwait(false);
            if (backlog is not { Count: > 0 })
                return; // an empty pass is free and must not consume the retry interval

            var now = _timeProvider.GetUtcNow();
            var stampKey = GetStateKey(EnvPendingJobKey, "pending-flush-at");
            if (_options.PendingAlerts.RetryInterval > TimeSpan.Zero)
            {
                var lastFlush = await GetStateAsync<DateTimeOffset>(stampKey, cancellationToken).ConfigureAwait(false);
                if (lastFlush is not null && now - lastFlush.Value < _options.PendingAlerts.RetryInterval)
                    return;
            }
            // Stamped before the pass: a crash mid-pass defers the remainder to the next interval rather
            // than re-sending the entries that were already retried.
            await SetStateAsync(stampKey, now, cancellationToken).ConfigureAwait(false);
            await FlushPendingAlertsAsync(EnvPendingJobKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _envFlushLock.Release();
        }
    }

    /// <summary>Retries alerts that no channel could deliver, oldest first; expired entries are dropped. Pending state lives in the job's state store, so durability follows the store implementation.</summary>
    private async Task FlushPendingAlertsAsync(string jobKey, CancellationToken cancellationToken)
    {
        var pendingKey = GetStateKey(jobKey, "pending-alerts");
        var pending = await _stateStore.GetCustomStateAsync<List<PendingAlert>>(pendingKey, cancellationToken).ConfigureAwait(false);
        if (pending is not { Count: > 0 })
            return;

        var now = _timeProvider.GetUtcNow();
        var survivors = new List<PendingAlert>();
        foreach (var entry in pending)
        {
            if (now - entry.FirstFailedAt > _options.PendingAlerts.Expiry)
            {
                _logger?.LogWarning(
                    "Dropping expired pending alert for channel {Channel} (correlation {CorrelationId}, first failed {FirstFailedAt}, {Attempts} attempts).",
                    entry.Channel, entry.CorrelationId, entry.FirstFailedAt, entry.Attempts);
                continue;
            }
            var result = await _dispatcher.DispatchAsync(entry.Channel, entry.ToMessage(), cancellationToken).ConfigureAwait(false);
            if (result.Success)
                _logger?.LogInformation(
                    "Resent previously undelivered alert to channel {Channel} (correlation {CorrelationId}).",
                    entry.Channel, entry.CorrelationId);
            else
                survivors.Add(entry with { Attempts = entry.Attempts + 1 });
        }

        if (survivors.Count == 0)
            await _stateStore.RemoveCustomStateAsync(pendingKey, cancellationToken).ConfigureAwait(false);
        else
            await _stateStore.SetCustomStateAsync(pendingKey, survivors, _options.PendingAlerts.Expiry, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordPendingAlertsAsync(string jobKey, NotificationMessage message, List<string> failedChannels, CancellationToken cancellationToken)
    {
        var pendingKey = GetStateKey(jobKey, "pending-alerts");
        var pending = await _stateStore.GetCustomStateAsync<List<PendingAlert>>(pendingKey, cancellationToken).ConfigureAwait(false) ?? [];
        var now = _timeProvider.GetUtcNow();
        foreach (var channel in failedChannels)
        {
            pending.RemoveAll(entry => string.Equals(entry.Channel, channel, StringComparison.Ordinal)
                && string.Equals(entry.CorrelationId, message.CorrelationId, StringComparison.Ordinal));
            pending.Add(PendingAlert.FromMessage(channel, message, now));
        }
        while (pending.Count > _options.PendingAlerts.MaxEntries)
        {
            _logger?.LogWarning(
                "Pending alert backlog exceeds {MaxEntries}; dropping the oldest entry (correlation {CorrelationId}).",
                _options.PendingAlerts.MaxEntries, pending[0].CorrelationId);
            pending.RemoveAt(0);
        }
        await _stateStore.SetCustomStateAsync(pendingKey, pending, _options.PendingAlerts.Expiry, cancellationToken).ConfigureAwait(false);
    }

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

    /// <summary>
    /// Gets or creates the per-job lock. Entries are never evicted and the cap is strict: eviction cannot be made atomic
    /// with a semaphore's waiters, so an evicted-but-acquired lock silently breaks overlap isolation. A full table means
    /// job names are unbounded (a configuration error) and must surface loudly instead of degrading in silence.
    /// </summary>
    private SemaphoreSlim GetJobLock(string jobKey)
    {
        if (_jobLocks.TryGetValue(jobKey, out var existing))
            return existing;
        if (_jobLocks.Count >= MaxJobLocks)
            throw new InvalidOperationException(
                $"Alert processor job-lock table is full ({MaxJobLocks} entries); job names must be a bounded fixed set — dynamic job names are not supported.");
        return _jobLocks.GetOrAdd(jobKey, static _ => new SemaphoreSlim(1, 1));
    }

    private sealed record StateValue<T>(T Value) where T : struct;

    private sealed record PendingAlert(
        string Channel,
        string Content,
        string? Title,
        string[] Recipients,
        string? TemplateCode,
        Dictionary<string, string>? TemplateParameters,
        NotificationSeverity Severity,
        string? CorrelationId,
        DateTimeOffset FirstFailedAt,
        int Attempts)
    {
        public static PendingAlert FromMessage(string channel, NotificationMessage message, DateTimeOffset firstFailedAt)
            => new(
                channel,
                message.Content,
                message.Title,
                message.Recipients?.ToArray() ?? [],
                message.TemplateCode,
                message.TemplateParameters is null ? null : new Dictionary<string, string>(message.TemplateParameters, StringComparer.Ordinal),
                message.Severity,
                message.CorrelationId,
                firstFailedAt,
                0);

        public NotificationMessage ToMessage()
            => new(
                Content,
                recipients: Recipients,
                title: Title,
                templateCode: TemplateCode,
                templateParameters: TemplateParameters,
                severity: Severity,
                source: "monitoring",
                correlationId: CorrelationId);
    }
}