using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching;
using PubSoft.Domain;
using PubSoft.Events;
using PubSoft.Mapping;
using PubSoft.Messaging;
using PubSoft.OAuth;
using PubSoft.Testing;

// Exercises the AOT-compatible leaf packages end-to-end under a real native publish.
// Serialization uses a source-generated context, matching the documented host contract.
var services = new ServiceCollection();
services.AddPubSoftInProcessDistributedLock();
services.AddPubSoftEvents();
services.AddSingleton<PubSoft.OAuth.IOAuthChannelConfigProvider, SmokeChannelConfigProvider>();
services.AddPubSoftOAuthProviders();
await using var provider = services.BuildServiceProvider();

if (provider.GetRequiredService<PubSoft.OAuth.IOAuthProviderRegistry>().GetAllProviders().Count != 19)
    return Fail("oauth registry");

var tenant = new AmbientTenantContext();
using (tenant.Change(7))
{
    if (tenant.TenantId != 7)
        return Fail("ambient tenant scope");
}

var moduleCalls = new List<string>();
var orchestrator = new PubSoft.Modularity.ModuleOrchestrator(
    new[]
    {
        new PubSoft.Modularity.ModuleDescriptor("smoke-core", typeof(SmokeCoreModule)),
        PubSoft.Modularity.ModuleDescriptor.FromType(typeof(SmokeDepModule), optional: true),
    },
    t => t == typeof(SmokeCoreModule)
        ? (PubSoft.Modularity.IPubSoftModule)new SmokeCoreModule()
        : new SmokeDepModule(moduleCalls));
var moduleCtx = new PubSoft.Modularity.ModuleContext { Services = services, ServiceProvider = provider };
orchestrator.ConfigureServicesAll(moduleCtx);
orchestrator.OnInitializationAll(moduleCtx);
orchestrator.OnShutdownAll(moduleCtx);
if (string.Join(',', moduleCalls) != "dep.init,dep.sd")
    return Fail("module lifecycle");

await using (await provider.GetRequiredService<IDistributedLock>()
    .AcquireAsync("smoke", TimeSpan.FromMinutes(1)) ?? throw new InvalidOperationException("lock"))
{
    var mapped = ObjectMapper.Map<SourceDto, DestDto>(new SourceDto { Name = "smoke", Count = 3 });
    if (mapped is not { Name: "smoke", Count: 3 })
    {
        Console.Error.WriteLine($"mapped={{Name={mapped?.Name},Count={mapped?.Count}}}");
        return Fail("object mapping");
    }

    var envelope = MessageEnvelope.Create("id-1", "smoke.ping", new Ping("hello"),
        DateTimeOffset.UtcNow, serializerOptions: SmokeJsonContext.Default.Options);
    var dispatcher = new MessageDispatcher(SmokeJsonContext.Default.Options);
    var handled = false;
    dispatcher.Register<Ping>("smoke.ping", new PingHandler(() => handled = true));
    await dispatcher.DispatchAsync(envelope);
    if (!handled)
        return Fail("message dispatch");
}

Console.WriteLine("PubSoft AOT smoke passed.");
return 0;

static int Fail(string stage)
{
    Console.Error.WriteLine($"AOT smoke failed at {stage}.");
    return 1;
}

public sealed class SourceDto
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public sealed class DestDto
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public sealed record Ping(string Text);

public sealed class SmokeCoreModule : PubSoft.Modularity.IPubSoftModule
{
    public void ConfigureServices(PubSoft.Modularity.ModuleContext context) { }
}

[PubSoft.Modularity.DependsOn(typeof(SmokeCoreModule))]
public sealed class SmokeDepModule(List<string> calls) : PubSoft.Modularity.IPubSoftModule
{
    public void ConfigureServices(PubSoft.Modularity.ModuleContext context) { }
    public void OnInitialization(PubSoft.Modularity.ModuleContext context) => calls.Add("dep.init");
    public void OnShutdown(PubSoft.Modularity.ModuleContext context) => calls.Add("dep.sd");
}

public sealed class PingHandler(Action onHandled) : IMessageHandler<Ping>
{
    public Task HandleAsync(Ping message, CancellationToken cancellationToken = default)
    {
        if (message.Text != "hello")
            throw new InvalidDataException("payload mismatch");
        onHandled();
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(Ping))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;

// Minimal fake so DI wiring for the OAuth providers resolves under AOT.
public sealed class SmokeChannelConfigProvider : PubSoft.OAuth.IOAuthChannelConfigProvider
{
    public Task<PubSoft.OAuth.OAuthChannelConfig?> GetByAppIdAsync(string appId, CancellationToken ct = default)
        => Task.FromResult<PubSoft.OAuth.OAuthChannelConfig?>(null);
    public Task<PubSoft.OAuth.OAuthChannelConfig?> GetDefaultAsync(string provider, string channelType, CancellationToken ct = default)
        => Task.FromResult<PubSoft.OAuth.OAuthChannelConfig?>(null);
    public Task<List<PubSoft.OAuth.OAuthChannelConfig>> ListByProviderAsync(string provider, CancellationToken ct = default)
        => Task.FromResult(new List<PubSoft.OAuth.OAuthChannelConfig>());
    public Task<List<PubSoft.OAuth.OAuthChannelConfig>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(new List<PubSoft.OAuth.OAuthChannelConfig>());
}
