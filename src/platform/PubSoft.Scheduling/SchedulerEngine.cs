using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Caching;

namespace PubSoft.Scheduling;

/// <summary>Runs due jobs each tick: distributed lease, overlap guard, timeout, retry, result processing.</summary>
public sealed class SchedulerEngine
{
    private readonly IExecutorRegistry _executors;
    private readonly IResultProcessorRegistry _processors;
    private readonly IStateStore _state;
    private readonly IDistributedLock? _distributedLock;
    private readonly ILogger<SchedulerEngine>? _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _jobLocks = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _misfireWatermarks = new(StringComparer.Ordinal);

    /// <summary>Creates the engine.</summary>
    public SchedulerEngine(
        IExecutorRegistry executors,
        IResultProcessorRegistry processors,
        IStateStore state,
        ILogger<SchedulerEngine>? logger = null,
        IDistributedLock? distributedLock = null)
    {
        _executors = executors;
        _processors = processors;
        _state = state;
        _logger = logger;
        _distributedLock = distributedLock;
    }

    /// <summary>Runs every job due at <paramref name="timestamp"/>, bounded by <see cref="SchedulerOptions.MaxConcurrentJobs"/>.</summary>
    public async Task RunDueJobsAsync(
        SchedulerOptions options,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid(out var error))
            throw new OptionsValidationException(nameof(SchedulerOptions), typeof(SchedulerOptions), [error]);

        var candidateJobs = options.Jobs
            .Where(job => job.Enabled
                && (job.Schedule.Matches(timestamp) || job.MisfirePolicy != JobMisfirePolicy.Ignore))
            .ToArray();
        using var concurrency = new SemaphoreSlim(options.MaxConcurrentJobs);
        await Task.WhenAll(candidateJobs.Select(async job =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteJobAsync(job, options, timestamp, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                concurrency.Release();
            }
        })).ConfigureAwait(false);
    }

    private async Task ExecuteJobAsync(
        ScheduledJob job,
        SchedulerOptions options,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var executor = _executors.Resolve(job.ExecutorType);
        if (executor is null)
        {
            _logger?.LogError("No executor registered for type {Type} (job {Name})", job.ExecutorType, job.Name);
            return;
        }

        SemaphoreSlim? overlapGuard = null;
        var overlapAcquired = false;
        IAsyncDisposable? instanceLock = null;
        JobResult? result = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (job.PreventOverlap)
            {
                overlapGuard = _jobLocks.GetOrAdd(job.Name, _ => new SemaphoreSlim(1, 1));
                if (!overlapGuard.Wait(0))
                {
                    _logger?.LogWarning("Job {Name} skipped: previous run still executing", job.Name);
                    return;
                }
                overlapAcquired = true;
            }

            var slot = await ResolveExecutionSlotAsync(job, options, timestamp, cancellationToken).ConfigureAwait(false);
            if (slot is null)
                return;

            // multi-instance guard: only the lease holder evaluates and runs this occurrence
            if (options.DistributedLocking)
            {
                if (_distributedLock is null)
                {
                    _logger?.LogWarning("Job {Name} skipped: DistributedLocking enabled but no IDistributedLock registered", job.Name);
                    return;
                }
                instanceLock = await _distributedLock.AcquireAsync(
                    $"pubsoft:sched:{job.Name}", options.DistributedLockLease, waitTimeout: TimeSpan.Zero, cancellationToken)
                    .ConfigureAwait(false);
                if (instanceLock is null)
                {
                    _logger?.LogDebug("Job {Name} skipped: another instance holds the lock", job.Name);
                    return;
                }

                // A contender may have completed the same missed occurrence while this instance waited
                // for (or failed) its first lock-free watermark read. Recheck inside the lease.
                if (slot.Value.IsMisfireRecovery)
                {
                    slot = await ResolveExecutionSlotAsync(job, options, timestamp, cancellationToken).ConfigureAwait(false);
                    if (slot is null)
                        return;
                }
            }

            var fencingToken = instanceLock is IFencedLockHandle fenced ? fenced.FencingToken : (long?)null;
            var executionContext = new JobExecutionContext(
                slot.Value.ScheduledTime,
                slot.Value.IsMisfireRecovery,
                fencingToken);
            result = await ExecuteWithRetryAsync(job, executor, executionContext, cancellationToken).ConfigureAwait(false);
            result = AttachExecutionMetadata(result, executionContext);
            if (await TryUpdateStateAsync(job, result, cancellationToken).ConfigureAwait(false)
                && job.MisfirePolicy == JobMisfirePolicy.FireOnce)
                RememberMisfireWatermark(job.Name, executionContext.ScheduledTime);
        }
        finally
        {
            // The overlap guard is released first: a failing lock release (Redis down)
            // must never leave the in-process guard held, which would skip this job forever.
            if (overlapAcquired)
                overlapGuard?.Release();
            if (instanceLock is not null)
            {
                try
                {
                    await instanceLock.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) // pubsoft:exempt PUBSOFT045 "best-effort lock release — the lease expiry is the backstop"
                {
                    _logger?.LogWarning(exception, "Job {Name} distributed lock release failed; the lease expires on its own.", job.Name);
                }
            }
        }

        stopwatch.Stop();
        if (result is null)
            return;
        _logger?.LogDebug("Job {Name} completed in {ElapsedMs}ms success={Success}",
            job.Name, stopwatch.ElapsedMilliseconds, result.Success);

        foreach (var processorName in job.ResultProcessors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processor = _processors.Resolve(processorName);
            if (processor is null)
            {
                _logger?.LogWarning("Result processor {Name} is not registered", processorName);
                continue;
            }

            try
            {
                await processor.ProcessAsync(job, result, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Result processor {Name} failed", processorName);
            }
        }
    }

    private async Task<ExecutionSlot?> ResolveExecutionSlotAsync(
        ScheduledJob job,
        SchedulerOptions options,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (job.Schedule.Matches(timestamp))
            return new ExecutionSlot(timestamp, IsMisfireRecovery: false);
        if (job.MisfirePolicy == JobMisfirePolicy.Ignore)
            return null;

        var latestMissed = job.Schedule.LastRunAtOrBefore(timestamp);
        if (latestMissed is null || timestamp - latestMissed.Value > options.MisfireWindow)
            return null;
        if (_misfireWatermarks.TryGetValue(job.Name, out var knownWatermark) && knownWatermark >= latestMissed.Value)
            return null;

        JobResult? lastResult;
        try
        {
            lastResult = await _state.GetLastResultAsync(job.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Job {Name} misfire catch-up skipped: execution watermark is unreadable.", job.Name);
            return null;
        }

        if (lastResult?.ScheduledTime is not { } completed)
            return null;
        RememberMisfireWatermark(job.Name, completed);
        return completed >= latestMissed.Value
            ? null
            : new ExecutionSlot(latestMissed.Value, IsMisfireRecovery: true);
    }

    private void RememberMisfireWatermark(string jobName, DateTimeOffset scheduledTime)
        => _misfireWatermarks.AddOrUpdate(
            jobName,
            scheduledTime,
            (_, current) => current >= scheduledTime ? current : scheduledTime);

    private async Task<bool> TryUpdateStateAsync(
        ScheduledJob job,
        JobResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _state.UpdateStateAsync(job.Name, result, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to update state for job {Name}", job.Name);
            return false;
        }
    }

    private static JobResult AttachExecutionMetadata(JobResult result, JobExecutionContext executionContext)
        => new()
        {
            Success = result.Success,
            StatusCode = result.StatusCode,
            DelayMs = result.DelayMs,
            ErrorMessage = result.ErrorMessage,
            Retryable = result.Retryable,
            Metadata = result.Metadata,
            ExecutedAt = result.ExecutedAt,
            ScheduledTime = executionContext.ScheduledTime,
            IsMisfireRecovery = executionContext.IsMisfireRecovery
        };

    /// <summary>
    /// Runs one execution attempt with the job timeout, retrying retryable failures
    /// and thrown exceptions per <see cref="ScheduledJob.Retry"/>.
    /// </summary>
    private async Task<JobResult> ExecuteWithRetryAsync(
        ScheduledJob job,
        IJobExecutor executor,
        JobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var retry = job.Retry;
        var maxAttempts = Math.Max(1, retry?.MaxAttempts ?? 1);

        for (var attempt = 1; ; attempt++)
        {
            JobResult? result = null;
            var canRetry = false;
            try
            {
                var jobTimeout = job.Timeout;
                using var timeout = jobTimeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : null;
                if (timeout is not null) timeout.CancelAfter(jobTimeout.GetValueOrDefault());
                result = await executor.ExecuteAsync(job, executionContext, timeout?.Token ?? cancellationToken).ConfigureAwait(false);
                canRetry = !result.Success && result.Retryable;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Job-level timeout fired while the host is still running.
                result = new JobResult { Success = false, StatusCode = 408, ErrorMessage = "Job timed out.", Retryable = true };
                canRetry = true;
                _logger?.LogWarning("Job {Name} timed out after {Timeout}", job.Name, job.Timeout);
            }
            catch (Exception exception)
            {
                result = new JobResult { Success = false, ErrorMessage = "Job execution failed." };
                canRetry = true;
                _logger?.LogError(exception, "Job {Name} threw an exception", job.Name);
            }

            if (!canRetry || attempt >= maxAttempts)
                return result!;

            var delay = retry is null
                ? TimeSpan.Zero
                : TimeSpan.FromMilliseconds(Math.Min(
                    retry.InitialDelay.TotalMilliseconds * Math.Pow(retry.BackoffFactor, attempt - 1),
                    retry.MaxDelay.TotalMilliseconds));
            _logger?.LogWarning("Job {Name} attempt {Attempt}/{Max} failed, retrying in {DelayMs}ms",
                job.Name, attempt, maxAttempts, delay.TotalMilliseconds);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private readonly record struct ExecutionSlot(DateTimeOffset ScheduledTime, bool IsMisfireRecovery);
}
