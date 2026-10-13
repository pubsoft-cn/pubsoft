using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditMiddlewareTests
{
    [Fact]
    public async Task MiddlewareWritesCompletedRequestSnapshotWithoutCapturingSensitiveMetadataByDefault()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        var sink = new TestAuditSnapshotSink();
        using var services = CreateServices(scope, sink);
        var context = CreateContext(services);
        context.Request.QueryString = new QueryString("?token=secret");
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Headers.UserAgent = "private-agent";
        context.Response.StatusCode = StatusCodes.Status200OK;
        var middleware = new AuditMiddleware(_ =>
        {
            scope.SetBusiness(category: "Record", action: "Create");
            context.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        var snapshot = Assert.Single(sink.Snapshots);
        Assert.Equal("/api/records", snapshot.Request.RequestPath);
        Assert.Null(snapshot.Request.ClientIp);
        Assert.Null(snapshot.Request.UserAgent);
        Assert.Null(snapshot.Request.Actor);
        Assert.DoesNotContain("token=secret", System.Text.Json.JsonSerializer.Serialize(snapshot));
        Assert.Equal(StatusCodes.Status201Created, snapshot.StatusCode);
    }

    [Fact]
    public async Task MiddlewareNormalizesMappedRemoteIpWithoutTrustingRawForwardedHeaders()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        var sink = new TestAuditSnapshotSink();
        using var services = CreateServices(scope, sink);
        var context = CreateContext(services);
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.20";
        var middleware = new AuditMiddleware(_ =>
        {
            scope.SetBusiness(action: "Read");
            return Task.CompletedTask;
        }, new AuditMiddlewareOptions { CaptureClientIp = true });

        await middleware.InvokeAsync(context);

        Assert.Equal("192.0.2.10", Assert.Single(sink.Snapshots).Request.ClientIp);
    }

    [Fact]
    public async Task MiddlewareCapturesHostResolvedActorOnlyWhenExplicitlyEnabled()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        var sink = new TestAuditSnapshotSink();
        using var services = CreateServices(scope, sink, new TestAuditActorAccessor(
            new AuditActorContext("user-7", "Ada", IsAdministrator: true)));
        var context = CreateContext(services);
        var middleware = new AuditMiddleware(_ =>
        {
            scope.SetBusiness(action: "Read");
            return Task.CompletedTask;
        }, new AuditMiddlewareOptions { CaptureActor = true });

        await middleware.InvokeAsync(context);

        var actor = Assert.Single(sink.Snapshots).Request.Actor;
        Assert.Equal("user-7", actor?.SubjectId);
        Assert.Equal("Ada", actor?.DisplayName);
        Assert.True(actor?.IsAdministrator);
    }

    [Fact]
    public async Task ActorAccessorFailureDoesNotInterruptRequestOrPersistAccessorException()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        var sink = new TestAuditSnapshotSink();
        using var services = CreateServices(scope, sink, new ThrowingAuditActorAccessor());
        var context = CreateContext(services);
        var middleware = new AuditMiddleware(_ =>
        {
            scope.SetBusiness(action: "Read");
            return Task.CompletedTask;
        }, new AuditMiddlewareOptions { CaptureActor = true });

        await middleware.InvokeAsync(context);

        var snapshot = Assert.Single(sink.Snapshots);
        Assert.Null(snapshot.Request.Actor);
        Assert.DoesNotContain("private actor accessor details", System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task MiddlewareFinalizesFailedRequestsAndRethrowsWithoutRecordingExceptionText()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        var sink = new TestAuditSnapshotSink();
        using var services = CreateServices(scope, sink);
        var context = CreateContext(services);
        var middleware = new AuditMiddleware(_ => throw new InvalidOperationException("secret exception detail"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("secret exception detail", exception.Message);
        var snapshot = Assert.Single(sink.Snapshots);
        Assert.False(snapshot.Success);
        Assert.Equal("RequestFailed", snapshot.FailureCode);
        Assert.DoesNotContain("secret exception detail", System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task MiddlewareRunsNormallyWhenAuditServicesAreNotRegistered()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services);
        var called = false;
        var middleware = new AuditMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task MiddlewareSinkFailureDoesNotReplaceSuccessfulResponse()
    {
        var scope = new AuditScope(new AuditChangeCollector());
        using var services = CreateServices(scope, new TestAuditSnapshotSink { ThrowOnWrite = true });
        var context = CreateContext(services);
        var middleware = new AuditMiddleware(_ =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task MiddlewareFinalizationFailureDoesNotReplaceSuccessfulResponse()
    {
        var scope = new AuditScope(new ThrowingAuditChangeCollector());
        using var services = CreateServices(scope, new TestAuditSnapshotSink());
        var context = CreateContext(services);
        var middleware = new AuditMiddleware(_ =>
        {
            scope.SetBusiness(action: "Write");
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    [Fact]
    public async Task DisabledAuditScopeDoesNotMaskApplicationExceptions()
    {
        var scope = new AuditScope(new AuditChangeCollector(), new AuditScopeOptions { Enabled = false });
        var sink = new TestAuditSnapshotSink();
        using var services = CreateServices(scope, sink);
        var context = CreateContext(services);
        var middleware = new AuditMiddleware(_ => throw new InvalidOperationException("business failure"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("business failure", exception.Message);
        Assert.Empty(sink.Snapshots);
    }

    private static ServiceProvider CreateServices(
        IAuditScope scope,
        IAuditSnapshotSink sink,
        IAuditActorAccessor? actorAccessor = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(scope)
            .AddSingleton(sink);
        if (actorAccessor is not null)
            services.AddSingleton(actorAccessor);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/records";
        return context;
    }

    private sealed class TestAuditSnapshotSink : IAuditSnapshotSink
    {
        public List<AuditRequestSnapshot> Snapshots { get; } = [];
        public bool ThrowOnWrite { get; init; }

        public ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnWrite)
                throw new InvalidOperationException("secret persistence detail");
            Snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAuditChangeCollector : IAuditChangeCollector
    {
        public IReadOnlyList<AuditEntityChange> GetSnapshot() => throw new InvalidOperationException("private collector details");
        public void Add(AuditEntityChange change) => throw new NotSupportedException();
        public void AddRange(IReadOnlyCollection<AuditEntityChange> changes) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }

    private sealed class TestAuditActorAccessor(AuditActorContext actor) : IAuditActorAccessor
    {
        public AuditActorContext GetCurrentActor() => actor;
    }

    private sealed class ThrowingAuditActorAccessor : IAuditActorAccessor
    {
        public AuditActorContext? GetCurrentActor() => throw new InvalidOperationException("private actor accessor details");
    }
}