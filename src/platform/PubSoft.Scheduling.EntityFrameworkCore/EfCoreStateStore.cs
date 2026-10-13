using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.Scheduling;

namespace PubSoft.Scheduling.EntityFrameworkCore;

public sealed class EfCoreStateStore<TDbContext> : IStateStore
    where TDbContext : DbContext
{
    private static readonly TimeSpan s_lastResultLifetime = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public EfCoreStateStore(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<JobResult?> GetLastResultAsync(string jobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        return GetAsync<JobResult>($"last:{jobName}", cancellationToken);
    }

    public Task UpdateStateAsync(string jobName, JobResult result, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(result);
        return SetAsync($"last:{jobName}", result, s_lastResultLifetime, cancellationToken);
    }

    public Task<T?> GetCustomStateAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetAsync<T>($"custom:{key}", cancellationToken);
    }

    public Task SetCustomStateAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (expiry <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(expiry));
        return SetAsync($"custom:{key}", value, expiry, cancellationToken);
    }

    public async Task RemoveCustomStateAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContext>();
        var context = services.GetRequiredService<TDbContext>();
        var identity = GetIdentity(tenant.TenantId, $"custom:{key}");
        var entries = await context.Set<SchedulerStateRecord>()
            .Where(record => record.TenantScope == identity.TenantScope
                && record.KeyHash == identity.KeyHash
                && record.Key == identity.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.RemoveRange(entries);
        if (entries.Count > 0)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContext>();
        var context = services.GetRequiredService<TDbContext>();
        var identity = GetIdentity(tenant.TenantId, key);
        var entries = context.Set<SchedulerStateRecord>()
            .Where(record => record.TenantScope == identity.TenantScope
                && record.KeyHash == identity.KeyHash
                && record.Key == identity.Key);
        var record = await entries
            .OrderByDescending(entry => entry.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
            return null;

        if (record.ExpiresAt is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
        {
            var expired = await entries.ToListAsync(cancellationToken).ConfigureAwait(false);
            context.RemoveRange(expired);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        return JsonSerializer.Deserialize<T>(record.ValueJson, s_jsonOptions);
    }

    private async Task SetAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken cancellationToken)
        where T : class
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContext>();
        var context = services.GetRequiredService<TDbContext>();
        var identity = GetIdentity(tenant.TenantId, key);
        var entries = context.Set<SchedulerStateRecord>()
            .Where(record => record.TenantScope == identity.TenantScope
                && record.KeyHash == identity.KeyHash
                && record.Key == identity.Key);
        var record = await entries
            .OrderByDescending(entry => entry.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        DateTimeOffset? expiresAt = expiry is { } lifetime ? now + lifetime : null;
        var serialized = JsonSerializer.Serialize(value, s_jsonOptions);

        if (record is null)
        {
            var newRecord = new SchedulerStateRecord
            {
                TenantId = tenant.TenantId,
                TenantScope = identity.TenantScope,
                Key = identity.Key,
                KeyHash = identity.KeyHash,
                ValueJson = serialized,
                UpdatedAt = now,
                ExpiresAt = expiresAt
            };
            context.Set<SchedulerStateRecord>().Add(newRecord);
            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException)
            {
                context.Entry(newRecord).State = EntityState.Detached;
                record = await entries
                    .OrderByDescending(entry => entry.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (record is null)
                    throw;
            }
        }

        record.ValueJson = serialized;
        record.UpdatedAt = now;
        record.ExpiresAt = expiresAt;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StateIdentity GetIdentity(long? tenantId, string key)
    {
        var tenantScope = tenantId is { } id
            ? $"tenant:{id.ToString(CultureInfo.InvariantCulture)}"
            : "host";
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return new StateIdentity(tenantId, tenantScope, key, keyHash);
    }

    private sealed record StateIdentity(long? TenantId, string TenantScope, string Key, string KeyHash);
}