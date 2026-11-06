using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Scheduling;

var services = new ServiceCollection();
services.AddPubSoftScheduler(options => options.Jobs.Add(new ScheduledJob
{
    Name = "daily-maintenance",
    ExecutorType = "maintenance",
    Schedule = ScheduleMasks.DailyAt(2, 30)
}));
if (!services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService))
    || !services.Any(descriptor => descriptor.ServiceType == typeof(IExecutorRegistry)))
    throw new InvalidOperationException("Scheduling package consumer check failed.");

var executor = new SmokeExecutor();
var processor = new SmokeResultProcessor();
var state = new MemoryStateStore();
var engine = new SchedulerEngine(
    new ExecutorRegistry([executor]),
    new ResultProcessorRegistry([processor]),
    state);
var job = new ScheduledJob
{
    Name = "consumer-smoke",
    ExecutorType = executor.Type,
    Schedule = ScheduleMask.EverySecond,
    ResultProcessors = [processor.Name]
};
await engine.RunDueJobsAsync(
    new SchedulerOptions { Jobs = [job] },
    new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

var lastResult = await state.GetLastResultAsync(job.Name, default);
if (executor.ExecutionCount != 1
    || processor.ProcessCount != 1
    || !processor.LastResultSuccess
    || lastResult is not { Success: true, StatusCode: 204 })
    throw new InvalidOperationException("Scheduling engine execution smoke check failed.");

Console.WriteLine("Isolated PubSoft.Scheduling consumer smoke check passed.");

sealed class SmokeExecutor : IJobExecutor
{
    public string Type => "smoke";
    public int ExecutionCount { get; private set; }

    public Task<JobResult> ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionCount++;
        return Task.FromResult(new JobResult { Success = true, StatusCode = 204 });
    }
}

sealed class SmokeResultProcessor : IResultProcessor
{
    public string Name => "smoke-result";
    public int ProcessCount { get; private set; }
    public bool LastResultSuccess { get; private set; }

    public Task ProcessAsync(ScheduledJob job, JobResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessCount++;
        LastResultSuccess = result.Success;
        return Task.CompletedTask;
    }
}