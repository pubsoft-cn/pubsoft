using System.Text.Json;

namespace PubSoft.Scheduling;


public interface IStateStore
{
    Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken);
    Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken);
    Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken) where T : class;
    Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken);
}
