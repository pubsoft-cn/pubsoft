using PubSoft.Auditing;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AuditScopeWaveTests
{
    private static AuditScope Scope(bool enabled = true, bool get = false) =>
        new(new AuditChangeCollector(), new AuditScopeOptions { Enabled = enabled, AuditGetRequests = get });

    private static AuditRequestContext Req(string m = "POST") =>
        new(DateTimeOffset.UtcNow, m, "/api/x", "10.0.0.1", "ua", new AuditActorContext("u1", "U One"));

    [Fact]
    public void IsEnabledGatedByInitialize()
    {
        var s = Scope();
        Assert.False(s.IsEnabled);
        s.Initialize(Req());
        Assert.True(s.IsEnabled);
    }

    [Fact]
    public void DisabledOptionsNeverEnabled()
    {
        var s = Scope(enabled: false);
        s.Initialize(Req());
        Assert.False(s.IsEnabled);
    }

    [Fact]
    public void FinalizeCapturesBusiness()
    {
        var s = Scope();
        s.Initialize(Req());
        s.SetBusiness(category: "c", action: "a", targetType: "t", targetId: "i", targetName: "n", detail: "d");
        var snap = s.Finalize(true, 200);
        Assert.Equal("c", snap!.Business.Category);
        Assert.Equal("a", snap.Business.Action);
        Assert.Equal("n", snap.Business.TargetName);
        Assert.True(snap.Success);
        Assert.True(s.IsFinalizing);
        Assert.Empty(snap.Changes); // collector untouched → empty list, snapshot still produced
    }

    [Fact]
    public void EmptySuccessfulPostElides()
    {
        // success + no changes + no business + non-GET → null (no audit noise)
        var s = Scope();
        s.Initialize(Req());
        Assert.Null(s.Finalize(true, 200));
    }

    [Fact]
    public void SuccessfulGetElidesUnlessOptIn()
    {
        var s = Scope();
        s.Initialize(Req("GET"));
        Assert.Null(s.Finalize(true, 200));
        var s2 = Scope(get: true);
        s2.Initialize(Req("GET"));
        s2.SetBusiness(action: "view");
        Assert.NotNull(s2.Finalize(true, 200));
    }

    [Fact]
    public void MarkFailureRecordsCode()
    {
        var s = Scope();
        s.Initialize(Req());
        s.MarkFailure("E42");
        var snap = s.Finalize(false, 500);
        Assert.False(snap!.Success);
        Assert.Equal("E42", snap.FailureCode);
        Assert.Equal(500, snap.StatusCode);
    }

    [Fact]
    public void HttpErrorDefaultsFailureCode()
    {
        var s = Scope();
        s.Initialize(Req());
        var snap = s.Finalize(false, 502);
        Assert.Equal("Http502", snap!.FailureCode); // no MarkFailure → Http{code} fallback
    }

    [Fact]
    public void FinalizeIsIdempotent()
    {
        var s = Scope();
        s.Initialize(Req());
        s.MarkFailure("E1");
        var a = s.Finalize(false, 500);
        Assert.Same(a, s.Finalize(false, 200)); // second call returns same snapshot
    }

    [Fact]
    public void ActorPropagatesToSnapshot()
    {
        var s = Scope();
        s.Initialize(Req());
        s.SetBusiness(action: "x"); // non-empty so snapshot isn't elided
        var snap = s.Finalize(true, 200);
        Assert.Equal("u1", snap!.Request.Actor?.SubjectId);
        Assert.Equal("U One", snap.Request.Actor?.DisplayName);
        Assert.Equal("/api/x", snap.Request.RequestPath);
    }

    [Fact]
    public void MutationsAfterFinalizeThrow()
    {
        var s = Scope();
        s.Initialize(Req());
        s.MarkFailure("E1");
        s.Finalize(false, 500);
        Assert.Throws<InvalidOperationException>(() => s.SetBusiness(action: "late"));
        Assert.Throws<InvalidOperationException>(() => s.MarkFailure("late"));
        Assert.Throws<InvalidOperationException>(() => s.Initialize(Req()));
    }
}
