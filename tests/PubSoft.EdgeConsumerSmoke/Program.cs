using Microsoft.Extensions.DependencyInjection;
using PubSoft.Edge;

// Session lifecycle + DI extension surface (gateway middleware surface is exercised in Core.Tests).
var store = new InMemoryEdgeSessionStore();
var manager = new EdgeSessionManager(store, TimeSpan.FromMinutes(5));
var key = await manager.CreateAsync("demo-app", "app-1", "token-1", userId: "user-1");
var session = await manager.ValidateAsync(key);
if (session is null || session.UserId != "user-1")
    throw new InvalidOperationException("Edge session consumer check failed.");
await manager.RevokeAsync(key);
if (await manager.ValidateAsync(key, autoRenew: false) is not null)
    throw new InvalidOperationException("Edge session revoke consumer check failed.");

var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
services.AddEdgeGateway().AddEdgeInMemorySession().AddEdgeRateLimiting(o => o.ClientPermitLimit = 10);
using var sp = services.BuildServiceProvider();
if (sp.GetService<EdgeHandlerRegistry>() is null || sp.GetService<EdgeSessionManager>() is null)
    throw new InvalidOperationException("Edge DI consumer check failed.");

Console.WriteLine("Isolated PubSoft.Edge consumer smoke check passed.");
