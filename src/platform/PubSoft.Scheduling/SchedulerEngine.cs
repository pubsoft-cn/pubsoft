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

        var stopwatch = Stopwatch.StartNew();
        JobResult result;
        try
        {
            result = await executor.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = new JobResult { Success = false, ErrorMessage = "Job execution failed." };
            _logger?.LogError(exception, "Job {Name} threw an exception", job.Name);
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
}