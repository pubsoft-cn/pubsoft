using Microsoft.Extensions.DependencyInjection;
using System.Net;
using PubSoft.Resilience.Http;

var requestCount = 0;
var serviceHandler = new RecordingHandler(() =>
    Interlocked.Increment(ref requestCount) == 1
        ? HttpStatusCode.ServiceUnavailable
        : HttpStatusCode.OK);
var services = new ServiceCollection();
services.AddHttpClient("consumer")
    .AddPubSoftHttpResilience(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(6);
        options.PerAttemptTimeout = TimeSpan.FromSeconds(2);
        options.Retry = new PubSoft.Resilience.RetryPolicy
        {
            MaxRetries = 1,
            InitialDelay = TimeSpan.Zero
        };
    })
    .ConfigurePrimaryHttpMessageHandler(() => serviceHandler);
using var provider = services.BuildServiceProvider();

var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("consumer");
using var response = await client.GetAsync("https://service.test/retry");
if (client.Timeout != Timeout.InfiniteTimeSpan
    || response.StatusCode != HttpStatusCode.OK
    || Volatile.Read(ref requestCount) != 2)
    throw new InvalidOperationException("HTTP resilience consumer smoke check failed.");

Console.WriteLine("Isolated PubSoft.Resilience.Http retry consumer smoke check passed.");

sealed class RecordingHandler(Func<HttpStatusCode> getStatusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(getStatusCode()));
    }
}