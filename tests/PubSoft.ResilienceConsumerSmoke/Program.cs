using PubSoft.Resilience;

var retryPolicy = new RetryPolicy { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(100) };
if (!retryPolicy.ShouldRetry(null, 1, 503)
    || retryPolicy.ShouldRetry(null, 1, 400)
    || retryPolicy.ShouldRetry(null, 3, 503)
    || retryPolicy.GetDelay(2) != TimeSpan.FromMilliseconds(200))
    throw new InvalidOperationException("Resilience retry policy smoke check failed.");

var breaker = new CircuitBreaker
{
    Options = new CircuitBreakerOptions { FailureThreshold = 2, OpenTimeout = TimeSpan.Zero }
};
breaker.RecordFailure("api.example.test");
breaker.RecordFailure("api.example.test");
if (breaker.GetState("api.example.test") != CircuitBreaker.State.Open
    || !breaker.TryAcquire("api.example.test")
    || breaker.GetState("api.example.test") != CircuitBreaker.State.HalfOpen
    || breaker.TryAcquire("api.example.test"))
    throw new InvalidOperationException("Resilience circuit breaker transition smoke check failed.");

breaker.RecordSuccess("api.example.test");
if (breaker.GetState("api.example.test") != CircuitBreaker.State.Closed)
    throw new InvalidOperationException("Resilience circuit breaker recovery smoke check failed.");

Console.WriteLine("Isolated PubSoft.Resilience consumer smoke check passed.");