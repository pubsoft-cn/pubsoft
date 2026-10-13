using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Resilience;
using PubSoft.Scheduling;
using PubSoft.Scheduling.Http;

var responses = new ConcurrentQueue<HttpStatusCode>([
    HttpStatusCode.OK,
    HttpStatusCode.ServiceUnavailable,
    HttpStatusCode.OK]);
var requestCount = 0;
var services = new ServiceCollection();
services.AddPubSoftHttpExecutors(options => options.DefaultTimeout = TimeSpan.FromSeconds(4));
services.AddHttpClient("PubSoft.Scheduling.Http")
    .ConfigurePrimaryHttpMessageHandler(() => new ResponseHandler(responses, request =>
    {
        var currentRequest = Interlocked.Increment(ref requestCount);
        if (currentRequest > 1 && (request.Method != HttpMethod.Post || request.Content is null))
            throw new InvalidOperationException("Unsafe retry consumer request was not configured as expected.");
    }));
using var provider = services.BuildServiceProvider();

var executors = provider.GetServices<IJobExecutor>().ToDictionary(executor => executor.Type);
if (!executors.TryGetValue("Http.Probe", out var probe)
    || !executors.TryGetValue("Http.Resilient", out var resilient))
    throw new InvalidOperationException("Scheduling.Http executor registration check failed.");

var probeResult = await probe.ExecuteAsync(CreateJob(new HttpExecutorConfig
{
    Url = "https://probe.example.test/health"
}), default);
var resilientResult = await resilient.ExecuteAsync(CreateJob(new HttpExecutorConfig
{
    Url = "https://service.example.test/action",
    Method = "POST",
    Body = "{\"action\":true}",
    Retry = new RetryPolicy { MaxRetries = 1, InitialDelay = TimeSpan.Zero },
    AllowUnsafeRetries = true
}), default);

if (!probeResult.Success
    || probeResult.StatusCode != (int)HttpStatusCode.OK
    || !resilientResult.Success
    || resilientResult.StatusCode != (int)HttpStatusCode.OK
    || Volatile.Read(ref requestCount) != 3)
    throw new InvalidOperationException("Scheduling.Http executor runtime check failed.");

Console.WriteLine("Isolated PubSoft.Scheduling.Http executor runtime check passed.");

static ScheduledJob CreateJob(HttpExecutorConfig config)
    => new() { ExecutorConfig = JsonSerializer.SerializeToElement(config) };

sealed class ResponseHandler(
    ConcurrentQueue<HttpStatusCode> responses,
    Action<HttpRequestMessage> validateRequest) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        validateRequest(request);
        if (!responses.TryDequeue(out var statusCode))
            throw new InvalidOperationException("Unexpected extra HTTP request.");
        return Task.FromResult(new HttpResponseMessage(statusCode));
    }
}