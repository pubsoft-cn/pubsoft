using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Scheduling;

public sealed class SchedulerEngine
{
    private readonly IExecutorRegistry _executors;
    private readonly IResultProcessorRegistry _processors;
    private readonly IStateStore _state;
    private readonly ILogger<SchedulerEngine>? _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _jobLocks = new();

    public SchedulerEngine(
        IExecutorRegistry executors,
        IResultProcessorRegistry processors,
        IStateStore state,
        ILogger<SchedulerEngine>? logger = null)
    {
        _executors = executors;
        _processors = processors;
        _state = state;
        _logger = logger;
    }

    public async Task RunDueJobsAsync(
        SchedulerOptions options,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid(out var error))
            throw new OptionsValidationException(nameof(SchedulerOptions), typeof(SchedulerOptions), [error]);

        var dueJobs = options.Jobs
            .Where(job => job.Enabled && job.Schedule.Matches(timestamp))
            .ToArray();
        using var concurrency = new SemaphoreSlim(options.MaxConcurrentJobs);
        await Task.WhenAll(dueJobs.Select(async job =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteJobAsync(job, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                concurrency.Release();
            }
        })).ConfigureAwait(false);
    }

    private async Task ExecuteJobAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        var executor = _executors.Resolve(job.ExecutorType);
        if (executor is null)
        {
            _logger?.LogError("No executor registered for type {Type} (job {Name})", job.ExecutorType, job.Name);
            return;
        }

        SemaphoreSlim? overlapGuard = null;
        if (job.PreventOverlap)
        {
            overlapGuard = _jobLocks.GetOrAdd(job.Name, _ => new SemaphoreSlim(1, 1));
            if (!overlapGuard.Wait(0))
            {
                _logger?.LogWarning("Job {Name} skipped: previous run still executing", job.Name);
                return;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        JobResult result;
        try
        {
            result = await ExecuteWithRetryAsync(job, executor, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            overlapGuard?.Release();
        }
        stopwatch.Stop();
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

        try
        {
            await _state.UpdateStateAsync(job.Name, result, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to update state for job {Name}", job.Name);
        }
    }

    /// <summary>
    /// Runs one execution attempt with the job timeout, retrying retryable failures
    /// and thrown exceptions per <see cref="ScheduledJob.Retry"/>.
    /// </summary>
    private async Task<JobResult> ExecuteWithRetryAsync(
        ScheduledJob job, IJobExecutor executor, CancellationToken cancellationToken)
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
                result = await executor.ExecuteAsync(job, timeout?.Token ?? cancellationToken).ConfigureAwait(false);
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
}
