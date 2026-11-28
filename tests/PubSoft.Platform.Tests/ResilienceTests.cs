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
    public void RetryPolicy_JitteredDelayStaysWithinConfiguredBounds()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            JitterRatio = 0.25
        };

        for (var index = 0; index < 100; index++)
        {
            var delay = policy.GetDelayWithJitter(1);
            Assert.InRange(delay, TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(1250));
        }
    }

    [Fact]
    public void RetryPolicy_ZeroJitterMatchesDeterministicDelay()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromMilliseconds(200),
            JitterRatio = 0
        };

        Assert.Equal(policy.GetDelay(2), policy.GetDelayWithJitter(2));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RetryPolicy_RejectsInvalidJitterRatio(double value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy { JitterRatio = value });

    [Fact]
    public void RetryPolicy_408And429AreExplicitOptIn()
    {
        var policy = new RetryPolicy();
        Assert.False(policy.ShouldRetry(null, 1, 408));
        Assert.False(policy.ShouldRetry(null, 1, 429));

        policy.RetryOnRequestTimeout = true;
        policy.RetryOnTooManyRequests = true;
        Assert.True(policy.ShouldRetry(null, 1, 408));
        Assert.True(policy.ShouldRetry(null, 1, 429));
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
    public void CircuitBreaker_RollingRatioRequiresMinimumThroughput()
    {
        var time = new MutableTimeProvider();
        var breaker = new CircuitBreaker(timeProvider: time)
        {
            Options = new CircuitBreakerOptions
            {
                Mode = CircuitBreakerMode.RollingFailureRatio,
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(30),
                OpenTimeout = TimeSpan.FromMinutes(1)
            }
        };

        breaker.RecordFailure("service");
        breaker.RecordFailure("service");
        Assert.Equal(CircuitBreaker.State.Closed, breaker.GetState("service"));

        breaker.RecordSuccess("service");
        breaker.RecordSuccess("service");
        breaker.RecordFailure("service");

        Assert.Equal(CircuitBreaker.State.Open, breaker.GetState("service"));
    }

    [Fact]
    public void CircuitBreaker_RollingRatioToleratesMixedTrafficBelowThreshold()
    {
        var time = new MutableTimeProvider();
        var breaker = new CircuitBreaker(timeProvider: time)
        {
            Options = new CircuitBreakerOptions
            {
                Mode = CircuitBreakerMode.RollingFailureRatio,
                FailureRatio = 0.75,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(30),
                OpenTimeout = TimeSpan.FromMinutes(1)
            }
        };

        for (var index = 0; index < 12; index++)
        {
            breaker.RecordFailure("service");
            breaker.RecordSuccess("service");
            breaker.RecordSuccess("service");
        }

        Assert.Equal(CircuitBreaker.State.Closed, breaker.GetState("service"));
    }

    [Fact]
    public void CircuitBreaker_RollingRatioExpiresOldSamples()
    {
        var time = new MutableTimeProvider();
        var breaker = new CircuitBreaker(timeProvider: time)
        {
            Options = new CircuitBreakerOptions
            {
                Mode = CircuitBreakerMode.RollingFailureRatio,
                FailureRatio = 0.75,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(10),
                OpenTimeout = TimeSpan.FromMinutes(1)
            }
        };

        breaker.RecordFailure("service");
        breaker.RecordFailure("service");
        breaker.RecordFailure("service");
        time.Advance(TimeSpan.FromSeconds(11));
        breaker.RecordSuccess("service");
        breaker.RecordSuccess("service");
        breaker.RecordSuccess("service");
        breaker.RecordFailure("service");

        Assert.Equal(CircuitBreaker.State.Closed, breaker.GetState("service"));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
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
