using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AmbientScopeContextTests
{
    [Fact]
    public void TenantContext_ChangeRestoresPreviousTenant()
    {
        var context = new AmbientTenantContext();
        Assert.Null(context.TenantId);

        using (context.Change(7))
        {
            Assert.Equal(7, context.TenantId);

            using (context.Change(13))
                Assert.Equal(13, context.TenantId);

            Assert.Equal(7, context.TenantId);
        }

        Assert.Null(context.TenantId);
    }

    [Fact]
    public async Task TenantContext_AmbientValueFlowsIntoAsyncContinuation()
    {
        var context = new AmbientTenantContext();
        using var scope = context.Change(7);
        await Task.Yield();
        Assert.Equal(7, context.TenantId);
    }

    [Fact]
    public async Task TenantContext_ParallelFlowsDoNotLeakBetweenEachOther()
    {
        var context = new AmbientTenantContext();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var scoped = Task.Run(async () =>
        {
            using var scope = context.Change(7);
            ready.TrySetResult();
            await release.Task;
            return context.TenantId;
        });

        await ready.Task;
        Assert.Null(context.TenantId);
        release.TrySetResult();
        Assert.Equal(7, await scoped);
    }

    [Fact]
    public void TenantContext_HostScopeIsExplicitNull()
    {
        var context = new AmbientTenantContext();
        using var outer = context.Change(7);
        using (context.Change(null))
            Assert.Null(context.TenantId);

        Assert.Equal(7, context.TenantId);
    }

    [Fact]
    public void MemberScopeContext_ChangeRestoresPreviousScope()
    {
        var context = new AmbientMemberScopeContext();
        Assert.Null(context.MemberId);
        Assert.False(context.IsMemberScoped);

        using (context.Change(10))
        {
            Assert.Equal(10, context.MemberId);
            Assert.True(context.IsMemberScoped);

            using (context.Change(null, isMemberScoped: false))
            {
                Assert.Null(context.MemberId);
                Assert.False(context.IsMemberScoped);
            }

            Assert.Equal(10, context.MemberId);
            Assert.True(context.IsMemberScoped);
        }

        Assert.Null(context.MemberId);
        Assert.False(context.IsMemberScoped);
    }

    [Fact]
    public void MemberScopeContext_ScopedWithoutMemberIdIsRepresentable()
    {
        var context = new AmbientMemberScopeContext();
        using var scope = context.Change(null, isMemberScoped: true);
        Assert.True(context.IsMemberScoped);
        Assert.Null(context.MemberId);
    }

    [Fact]
    public void Scope_DisposeIsIdempotent()
    {
        var context = new AmbientTenantContext();
        var scope = context.Change(7);
        scope.Dispose();
        scope.Dispose();
        Assert.Null(context.TenantId);
    }
}
