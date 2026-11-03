using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RequestSaveChangesMiddlewareTests
{
    [Fact]
    public async Task Middleware_SavesCallbacksInOrderAfterSuccessfulRequest()
    {
        var calls = new List<int>();
        var saveChanges = new PubSoftRequestSaveChanges();
        saveChanges.Register(_ =>
        {
            calls.Add(1);
            return Task.CompletedTask;
        });
        saveChanges.Register(_ =>
        {
            calls.Add(2);
            return Task.CompletedTask;
        });
        var context = CreateContext(saveChanges);
        var middleware = new PubSoftRequestSaveChangesMiddleware(_ =>
        {
            Assert.Empty(calls);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public async Task Middleware_SavesBeforeSuccessfulResponseStarts()
    {
        var context = CreateContext(new PubSoftRequestSaveChanges());
        var responseFeature = new TestResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        var saveCalled = false;
        context.RequestServices.GetRequiredService<PubSoftRequestSaveChanges>().Register(_ =>
        {
            Assert.False(context.Response.HasStarted);
            saveCalled = true;
            return Task.CompletedTask;
        });
        var middleware = new PubSoftRequestSaveChangesMiddleware(async httpContext =>
        {
            await responseFeature.StartAsync();
            Assert.True(saveCalled);
        });

        await middleware.InvokeAsync(context);

        Assert.True(context.Response.HasStarted);
    }

    [Fact]
    public async Task Middleware_SaveFailurePreventsSuccessfulResponseFromStarting()
    {
        var context = CreateContext(new PubSoftRequestSaveChanges());
        var responseFeature = new TestResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.RequestServices.GetRequiredService<PubSoftRequestSaveChanges>().Register(_ =>
            Task.FromException(new InvalidOperationException("save failed")));
        var middleware = new PubSoftRequestSaveChangesMiddleware(_ => responseFeature.StartAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.False(responseFeature.HasStarted);
    }

    [Fact]
    public async Task Middleware_DoesNotSaveWhenRequestThrows()
    {
        var saveCalled = false;
        var saveChanges = new PubSoftRequestSaveChanges();
        saveChanges.Register(_ =>
        {
            saveCalled = true;
            return Task.CompletedTask;
        });
        var middleware = new PubSoftRequestSaveChangesMiddleware(_ =>
            throw new InvalidOperationException("request failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(CreateContext(saveChanges)));

        Assert.False(saveCalled);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task Middleware_DoesNotSaveForErrorResponses(int statusCode)
    {
        var saveCalled = false;
        var saveChanges = new PubSoftRequestSaveChanges();
        saveChanges.Register(_ =>
        {
            saveCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(saveChanges);
        var middleware = new PubSoftRequestSaveChangesMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(saveCalled);
    }

    [Fact]
    public async Task Coordinator_ExecutesCallbacksAtMostOnce()
    {
        var calls = 0;
        var saveChanges = new PubSoftRequestSaveChanges();
        saveChanges.Register(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await saveChanges.SaveAllAsync();
        await saveChanges.SaveAllAsync();

        Assert.Equal(1, calls);
        Assert.Throws<InvalidOperationException>(() => saveChanges.Register(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task Coordinator_ConcurrentSaveCallsAwaitTheSameCompletion()
    {
        var callbackStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var saveChanges = new PubSoftRequestSaveChanges();
        saveChanges.Register(async _ =>
        {
            Interlocked.Increment(ref callCount);
            callbackStarted.SetResult(true);
            await releaseCallback.Task;
        });

        var firstSave = saveChanges.SaveAllAsync();
        await callbackStarted.Task;
        var secondSave = saveChanges.SaveAllAsync();

        Assert.False(firstSave.IsCompleted);
        Assert.False(secondSave.IsCompleted);
        releaseCallback.SetResult(true);
        await Task.WhenAll(firstSave, secondSave);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Registration_UsesScopedCoordinator()
    {
        var services = new ServiceCollection();
        services.AddPubSoftRequestSaveChanges();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<PubSoftRequestSaveChanges>(),
            secondScope.ServiceProvider.GetRequiredService<PubSoftRequestSaveChanges>());
    }

    private static DefaultHttpContext CreateContext(PubSoftRequestSaveChanges saveChanges)
    {
        var services = new ServiceCollection()
            .AddSingleton(saveChanges)
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private sealed class TestResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
            => _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task StartAsync()
        {
            for (var index = _onStarting.Count - 1; index >= 0; index--)
                await _onStarting[index].Callback(_onStarting[index].State);

            HasStarted = true;
        }
    }
}