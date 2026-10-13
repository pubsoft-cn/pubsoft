using PubSoft.Core;

var success = Result<int>.Ok(20)
    .Map(value => value * 2)
    .Bind(value => Result<int>.Ok(value + 2));
if (!success.IsOk || success.Value != 42)
    throw new InvalidOperationException("Core result success smoke check failed.");

var failure = Result<int>.Fail("invalid", "Input was rejected.").Map(value => value + 1);
if (failure.IsOk || failure.Error.Code != "invalid")
    throw new InvalidOperationException("Core result error propagation smoke check failed.");

var total = Money.FromYuan(12.34m) + Money.FromFen(66);
if (total.Fen != 1300 || total.ToYuan() != 13m)
    throw new InvalidOperationException("Core money arithmetic smoke check failed.");

Console.WriteLine("Isolated PubSoft.Core consumer smoke check passed.");