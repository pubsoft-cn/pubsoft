namespace PubSoft.Resilience;

/// <summary>Host selection strategy.</summary>
public enum LoadBalanceStrategy : byte
{
    /// <summary>Rotate hosts in order.</summary>
    RoundRobin,
    /// <summary>Pick a host uniformly at random.</summary>
    Random,
    /// <summary>Pick the less-loaded of two random hosts.</summary>
    PowerOfTwoChoices,
}

/// <summary>Selects a host from a candidate list, tracking in-flight counts for least-loaded choices.</summary>
public sealed class LoadBalancer
{
    private int _roundRobinIndex;
    private readonly Dictionary<string, int> _concurrentRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Chooses a host; call <see cref="DecrementRequest"/> when the request finishes.</summary>
    public string SelectHost(IReadOnlyList<string> hosts, LoadBalanceStrategy strategy = LoadBalanceStrategy.RoundRobin)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        if (hosts.Count == 0) throw new ArgumentException("Hosts must not be empty.", nameof(hosts));
        if (hosts.Count == 1) return hosts[0];

        return strategy switch
        {
            LoadBalanceStrategy.Random => hosts[Random.Shared.Next(hosts.Count)],
            LoadBalanceStrategy.PowerOfTwoChoices => SelectPowerOfTwoChoices(hosts),
            _ => hosts[(int)((uint)Interlocked.Increment(ref _roundRobinIndex) % (uint)hosts.Count)],
        };
    }

    /// <summary>Counts an in-flight request against a host.</summary>
    public void IncrementRequest(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        lock (_lock)
        {
            _concurrentRequests.TryGetValue(host, out var count);
            _concurrentRequests[host] = count + 1;
        }
    }

    /// <summary>Releases an in-flight request slot.</summary>
    public void DecrementRequest(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        lock (_lock)
        {
            if (!_concurrentRequests.TryGetValue(host, out var count)) return;
            if (count <= 1) _concurrentRequests.Remove(host);
            else _concurrentRequests[host] = count - 1;
        }
    }

    private string SelectPowerOfTwoChoices(IReadOnlyList<string> hosts)
    {
        var firstIndex = Random.Shared.Next(hosts.Count);
        var secondIndex = Random.Shared.Next(hosts.Count - 1);
        if (secondIndex >= firstIndex) secondIndex++;

        var firstHost = hosts[firstIndex];
        var secondHost = hosts[secondIndex];
        lock (_lock)
        {
            var firstCount = _concurrentRequests.GetValueOrDefault(firstHost);
            var secondCount = _concurrentRequests.GetValueOrDefault(secondHost);
            return firstCount <= secondCount ? firstHost : secondHost;
        }
    }
}
