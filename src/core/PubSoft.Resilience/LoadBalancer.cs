namespace PubSoft.Resilience;

public enum LoadBalanceStrategy : byte
{
    RoundRobin,
    Random,
    PowerOfTwoChoices,
}

public sealed class LoadBalancer
{
    private int _roundRobinIndex;
    private readonly Dictionary<string, int> _concurrentRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

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

    public void IncrementRequest(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        lock (_lock)
        {
            _concurrentRequests.TryGetValue(host, out var count);
            _concurrentRequests[host] = count + 1;
        }
    }

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
