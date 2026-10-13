using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.Testing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SaveParticipantDiscoveryTests
{
    [Fact]
    public async Task Middleware_EnrollsRegisteredServiceParticipantsAfterExplicitCallbacks()
    {
        var calls = new List<string>();
        var services = new ServiceCollection()
            .AddScoped(_ => new PubSoftRequestSaveChanges())
            .AddScoped<IPubSoftSaveParticipant>(_ => new DelegateSaveParticipant(_ =>
            {
                calls.Add("participant");
                return Task.FromResult(0);
            }))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.RequestServices.GetRequiredService<PubSoftRequestSaveChanges>()
            .Register(_ => { calls.Add("explicit"); return Task.CompletedTask; });

        var middleware = new PubSoftRequestSaveChangesMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal(["explicit", "participant"], calls);
    }

    [Fact]
    public async Task Coordinator_ServiceParticipantsAreEnrolledExactlyOnce()
    {
        var saves = 0;
        var coordinator = new PubSoftRequestSaveChanges();
        IPubSoftSaveParticipant participant = new DelegateSaveParticipant(_ =>
        {
            saves++;
            return Task.FromResult(0);
        });

        coordinator.RegisterServiceParticipants([participant]);
        coordinator.RegisterServiceParticipants([participant]);
        await coordinator.SaveAllAsync();

        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Coordinator_ServiceParticipantsAfterSaveStartedAreSkipped()
    {
        var saves = 0;
        var coordinator = new PubSoftRequestSaveChanges();
        await coordinator.SaveAllAsync();

        coordinator.RegisterServiceParticipants(
            [new DelegateSaveParticipant(_ => { saves++; return Task.FromResult(0); })]);

        Assert.Equal(0, saves);
    }

    [Fact]
    public void EfCoreExtensions_SaveParticipantResolvesTheDbContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new FixedTenantContext(1));
        services.AddDbContext<ParticipantDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddPubSoftSaveParticipant<ParticipantDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var participants = scope.ServiceProvider.GetServices<IPubSoftSaveParticipant>().ToList();
        var context = scope.ServiceProvider.GetRequiredService<ParticipantDbContext>();

        Assert.Same(context, Assert.Single(participants));
    }

    [Fact]
    public void EfCoreExtensions_RequestUnitOfWorkRegistersUowAndParticipant()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new FixedTenantContext(1));
        services.AddDbContext<ParticipantDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddPubSoftRequestUnitOfWork<ParticipantDbContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<PubSoftUnitOfWork<ParticipantDbContext>>(
            scope.ServiceProvider.GetRequiredService<IPubSoftUnitOfWork<ParticipantDbContext>>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<ParticipantDbContext>(),
            Assert.Single(scope.ServiceProvider.GetServices<IPubSoftSaveParticipant>()));
    }

    private sealed class ParticipantDbContext(DbContextOptions<ParticipantDbContext> options, ITenantContext tenantContext)
        : PubSoftDbContext(options, tenantContext);
}
