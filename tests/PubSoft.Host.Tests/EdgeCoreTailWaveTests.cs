using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class EdgeCoreTailWaveTests
{
    [Fact]
    public void ErrorCodeConstantsAreStable()
    {
        Assert.Equal("10400", EdgeErrorCodes.MethodRequired);
        Assert.Equal("10401", EdgeErrorCodes.SignatureInvalid);
        Assert.Equal("10401", EdgeErrorCodes.NonceRequired);
        Assert.Equal("10401", EdgeErrorCodes.SessionKeyExpired);
        Assert.Equal("10400", EdgeErrorCodes.ParamsValidationFailed);
        Assert.Equal("10400", EdgeErrorCodes.TimestampOutOfRange);
        Assert.Equal("10401", EdgeErrorCodes.SchemeNotAccepted);
        Assert.Equal("10401", EdgeErrorCodes.ClientNotConfigured);
        Assert.Equal("10401", EdgeErrorCodes.AuthError);
        Assert.Equal("10403", EdgeErrorCodes.RoleRequired);
        Assert.Equal("10403", EdgeErrorCodes.TenantMismatch);
        Assert.Equal("10404", EdgeErrorCodes.MethodNotFound);
        Assert.Equal("10409", EdgeErrorCodes.DuplicateRequest);
        Assert.Equal("10429", EdgeErrorCodes.FailureRateLimited);
        Assert.Equal("10500", EdgeErrorCodes.SignServiceNotConfigured);
        Assert.Equal("10500", EdgeErrorCodes.SessionServiceNotConfigured);
        Assert.Equal("10401", EdgeErrorCodes.UnknownScheme);
        Assert.Equal("10400", EdgeErrorCodes.ClientIdRequired);
        Assert.Equal("10400", EdgeErrorCodes.ParamsInvalid);
        Assert.Equal("10401", EdgeErrorCodes.SessionKeyRequired);
        Assert.Equal("10401", EdgeErrorCodes.SessionKeyInvalid);
    }

    [Fact]
    public void RpcResponseFactoriesAndEquality()
    {
        var ok = EdgeRpcResponse.Success(new { v = 1 });
        Assert.True(ok.Ok);
        Assert.NotNull(ok.Data);
        var err = EdgeRpcResponse.Error("10400", "bad");
        Assert.False(err.Ok);
        Assert.Equal("10400", err.Code);
        var sub = EdgeRpcResponse.ErrorWithSub("10400", "bad", "B1", "detail");
        Assert.Equal("B1", sub.SubCode);
        Assert.Equal("detail", sub.SubMessage);
        Assert.Equal(EdgeRpcResponse.Error("1", "m"), EdgeRpcResponse.Error("1", "m"));
    }

    [Fact]
    public void WithDiagnosticsStampsRequestContext()
    {
        var req = new EdgeRpcRequest { RequestId = "r-1", Method = "m" };
        var noActivity = EdgeRpcResponse.Error("x", "y").WithDiagnostics(req);
        Assert.Equal("r-1", noActivity.RequestId);
        Assert.Null(noActivity.TraceId); // TraceId comes from Activity.Current — null without ambient span
        using var source = new System.Diagnostics.ActivitySource("test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("op");
        var withActivity = EdgeRpcResponse.Error("x", "y").WithDiagnostics(req);
        Assert.Equal(activity!.TraceId.ToString(), withActivity.TraceId);
    }

    [Fact]
    public void EdgeRpcRequestDefaults()
    {
        var r = new EdgeRpcRequest();
        Assert.Equal(EdgeAuthScheme.Hmac, r.Scheme);
        Assert.Equal(string.Empty, r.ClientId);
        Assert.Equal(string.Empty, r.Nonce);
        Assert.Null(r.RequestId);
    }

    [Fact]
    public void EdgeSignResultRecord()
    {
        var ok = new EdgeSignResult(true, null, "payload");
        Assert.True(ok.Success);
        Assert.Null(ok.ErrorMessage);
        var fail = new EdgeSignResult(false, "sig bad", null);
        Assert.Equal("sig bad", fail.ErrorMessage);
    }
}
