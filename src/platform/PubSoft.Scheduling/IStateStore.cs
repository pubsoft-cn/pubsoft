using System.Text.Json;

namespace PubSoft.Scheduling;


/// <summary>Scheduler persistence: last results + job-custom state.</summary>
public interface IStateStore
{
    /// <summary>Last recorded result for a job.</summary>
    Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken);
    /// <summary>Persists a job's last result.</summary>
    Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken);
    /// <summary>Reads a job's custom state value.</summary>
    Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    /// <summary>Writes a job's custom state value with optional expiry.</summary>
    Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken) where T : class;
    /// <summary>Deletes a job's custom state value.</summary>
    Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken);
}
