using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.MultiTenancy;

namespace PubSoft.Messaging.Outbox;

/// <summary>
/// Catalog-backed <see cref="IOutboxScopeProvider"/> that enumerates enabled tenants from
/// <see cref="ITenantAppService"/>, creates an isolated scope for each, and initializes that scope
/// through <see cref="IOutboxTenantScopeInitializer"/> before yielding it. Disabled tenants are never
/// enumerated. Tenant authorization/catalog visibility remains the catalog implementation's trust boundary.
/// </summary>
public sealed class TenantOutboxScopeProvider : IOutboxScopeProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxTenantScopeInitializer _initializer;
    private readonly TenantOutboxScopeOptions _options;

    /// <summary>Creates the provider.</summary>
    public TenantOutboxScopeProvider(
        IServiceScopeFactory scopeFactory,
        IOutboxTenantScopeInitializer initializer,
        TenantOutboxScopeOptions? options = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _options = options ?? new TenantOutboxScopeOptions();
        if (!_options.IsValid(out var error))
            throw new ArgumentException(error, nameof(options));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tenantIds = new List<long?>();
        if (_options.IncludeHostScope)
            tenantIds.Add(null);

        await using (var catalogScope = _scopeFactory.CreateAsyncScope())
        {
            var catalog = catalogScope.ServiceProvider.GetRequiredService<ITenantAppService>();
            var tenants = await catalog.GetListAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            tenantIds.AddRange(tenants
                .Where(tenant => tenant.IsEnabled)
                .Select(tenant => (long?)tenant.Id)
                .Distinct()
                .OrderBy(id => id));
        }

        if (tenantIds.Where(id => id.HasValue).Skip(_options.MaxTenantsPerPoll).Any())
            throw new InvalidOperationException(
                $"Tenant outbox enumeration exceeds MaxTenantsPerPoll ({_options.MaxTenantsPerPoll}); refusing to silently skip tenants.");

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new ScopeLease(_scopeFactory.CreateAsyncScope());
            try
            {
                await _initializer.InitializeAsync(lease.ServiceProvider, tenantId, cancellationToken).ConfigureAwait(false);
                yield return lease.AsAsyncServiceScope();
            }
            finally
            {
                if (!lease.DisposeRequested)
                    await lease.DisposeRealAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class ScopeLease : IServiceScope, IAsyncDisposable, IDisposable
    {
        private readonly AsyncServiceScope _inner;
        private int _disposeRequested;
        private int _disposed;

        public ScopeLease(AsyncServiceScope inner) => _inner = inner;

        public IServiceProvider ServiceProvider => _inner.ServiceProvider;
        public bool DisposeRequested => Volatile.Read(ref _disposeRequested) != 0;

        public AsyncServiceScope AsAsyncServiceScope() => new(this);

        public void Dispose()
        {
            MarkDisposeRequested();
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _inner.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            MarkDisposeRequested();
            await DisposeRealAsync().ConfigureAwait(false);
        }

        internal async ValueTask DisposeRealAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await _inner.DisposeAsync().ConfigureAwait(false);
        }

        private void MarkDisposeRequested() => Interlocked.Exchange(ref _disposeRequested, 1);

    }
}
