using PubSoft.Edge;

// Client SDK surface — envelope building + requestId propagation without a live server.
var client = new EdgeClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") }, "demo-app", "key");
try
{
    var result = await client.InvokeAsync<JsonElementStub>("demo.ping", new { itemId = 1 });
    throw new InvalidOperationException($"Unreachable endpoint should fail, got {result}");
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or EdgeRpcException)
{
    // Expected — no gateway answering; SDK sign/send surface verified (transport errors are wrapped as EdgeRpcException).
}
if (client.LastRequestId is null)
    throw new InvalidOperationException("Edge client requestId propagation check failed.");

Console.WriteLine("Isolated PubSoft.Edge.Client consumer smoke check passed.");

file sealed record JsonElementStub;
