using PubSoft.Resilience;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ResilienceTests
{
    [Fact]
    public void RetryPolicy_AllowsConfiguredRetryCount()
    {
        var policy = new RetryPolicy { MaxRetries = 1 };

        Assert.True(policy.ShouldRetry(null, attempt: 1, statusCode: 503));
        Assert.False(policy.ShouldRetry(null, attempt: 2, statusCode: 503));
        Assert.False(policy.ShouldRetry(null, attempt: 1, statusCode: 400));
    }

    [Fact]
    public void RetryPolicy_UsesCappedExponentialBackoff()
    {
        var policy = new RetryPolicy { InitialDelay = TimeSpan.FromSeconds(1) };

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(10));
    }

    [Fact]
    public void CircuitBreaker_AllowsOnlyOneHalfOpenProbe()
    {
        var breaker = new CircuitBreaker
        {
            Options = new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                OpenTimeout = TimeSpan.Zero,
            },
        };

        breaker.RecordFailure("service");
        var acceptedProbes = 0;
        Parallel.For(0, 100, _ =>
        {
            if (breaker.TryAcquire("service"))
                Interlocked.Increment(ref acceptedProbes);
        });

        Assert.Equal(1, acceptedProbes);
        Assert.Equal(CircuitBreaker.State.HalfOpen, breaker.GetState("service"));
        breaker.RecordSuccess("service");
        Assert.Equal(CircuitBreaker.State.Closed, breaker.GetState("service"));
    }

    [Fact]
    public void LoadBalancer_RoundRobinSelectsEveryHost()
    {
        var balancer = new LoadBalancer();
        var hosts = new[] { "a", "b", "c" };
        var selected = Enumerable.Range(0, 6)
            .Select(_ => balancer.SelectHost(hosts))
            .ToArray();

        Assert.Equal(2, selected.Count(host => host == "a"));
        Assert.Equal(2, selected.Count(host => host == "b"));
        Assert.Equal(2, selected.Count(host => host == "c"));
    }

    [Fact]
    public void LoadBalancer_PowerOfTwoChoicesPrefersLessLoadedHost()
    {
        var balancer = new LoadBalancer();
        var hosts = new[] { "busy", "idle" };
        for (var index = 0; index < 10; index++)
            balancer.IncrementRequest("busy");

        Assert.Equal("idle", balancer.SelectHost(hosts, LoadBalanceStrategy.PowerOfTwoChoices));
    }
}
