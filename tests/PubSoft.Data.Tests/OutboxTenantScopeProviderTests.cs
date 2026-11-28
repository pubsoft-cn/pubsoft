using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;
using PubSoft.Messaging.Outbox;
using PubSoft.MultiTenancy;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Data.Tests;

public sealed class OutboxTenantScopeProviderTests
{
    [Fact]
    public async Task ProviderEnumeratesHostAndEnabledTenantsWithInitializedScopedContext()
    {
        var services = CreateServices([
            Tenant(3, enabled: true), Tenant(1, enabled: true), Tenant(2, enabled: false)
        ]);
        var provider = new TenantOutboxScopeProvider(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ScopedTenantOutboxScopeInitializer(),
            new TenantOutboxScopeOptions { IncludeHostScope = true });

        var seen = new List<long?>();
        await foreach (var scope in provider.CreateScopesAsync())
        {
            await using (scope)
                seen.Add(scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId);
        }

        Assert.Equal([null, 1, 3], seen);
    }

    [Fact]
    public async Task ProviderFailsClosedWhenEnabledTenantCountExceedsBound()
    {
        var services = CreateServices([Tenant(1, true), Tenant(2, true)]);
        var provider = new TenantOutboxScopeProvider(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ScopedTenantOutboxScopeInitializer(),
            new TenantOutboxScopeOptions { MaxTenantsPerPoll = 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var scope in provider.CreateScopesAsync())
                await scope.DisposeAsync();
        });
    }

    [Fact]
    public async Task InitializerRejectsATenantContextThatCannotBeOwnedByTheScope()
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(new FixedTenantContext(null))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ScopedTenantOutboxScopeInitializer().InitializeAsync(scope.ServiceProvider, 7, default));
    }

    [Fact]
    public void ScopedTenantContextIsOneShot()
    {
        var context = new ScopedTenantContext();
        context.Initialize(null);
        Assert.Null(context.TenantId);
        Assert.Throws<InvalidOperationException>(() => context.Initialize(1));
    }

    private static ServiceProvider CreateServices(IReadOnlyList<TenantDto> tenants)
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopedTenantContext>());
        services.AddScoped<ITenantAppService>(_ => new TenantCatalog(tenants));
        return services.BuildServiceProvider();
    }

    private static TenantDto Tenant(long id, bool enabled) => new()
    {
        Id = id,
        Code = $"t{id}",
        Name = $"tenant-{id}",
        DisplayName = $"Tenant {id}",
        IsEnabled = enabled,
    };

    private sealed class TenantCatalog(IReadOnlyList<TenantDto> tenants) : ITenantAppService
    {
        public Task<List<TenantDto>> GetListAsync() => Task.FromResult(tenants.ToList());
        public Task<TenantDto> GetAsync(long id) => throw new NotSupportedException();
        public Task<TenantDto> CreateAsync(CreateTenantInput input) => throw new NotSupportedException();
        public Task<TenantDto> UpdateAsync(long id, UpdateTenantInput input) => throw new NotSupportedException();
        public Task DeleteAsync(long id) => throw new NotSupportedException();
    }
}
