using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

/// <summary>Topologically orders modules and runs their lifecycle.</summary>
public sealed class ModuleOrchestrator
{
    private readonly ModuleDescriptor[] _modules;
    private readonly Dictionary<Type, ModuleDescriptor> _byType;
    private readonly Type[] _topologicalOrder;
    private readonly Type[] _shutdownOrder;
    private readonly Dictionary<Type, IPubSoftModule> _instances = new();
    private readonly Dictionary<string, string> _failedModules = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _failedTypes = new();
    private readonly Func<Type, IPubSoftModule> _moduleFactory;

    /// <summary>Creates the orchestrator over discovered modules.</summary>
    public ModuleOrchestrator(
        ModuleDescriptor[] modules,
        Func<Type, IPubSoftModule> moduleFactory,
        Type[]? topologicalOrder = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(moduleFactory);
        var duplicateName = modules.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Skip(1).Any());
        if (duplicateName is not null)
            throw new ArgumentException($"duplicate module name: {duplicateName.Key}", nameof(modules));
        _modules = modules;
        _byType = modules.ToDictionary(m => m.ModuleType);
        _moduleFactory = moduleFactory;
        _topologicalOrder = topologicalOrder ?? TopologicalSort(modules);
        _shutdownOrder = (Type[])_topologicalOrder.Clone();
        Array.Reverse(_shutdownOrder);
    }

    /// <summary>Loaded module count.</summary>
    public int ModuleCount => _modules.Length;

    /// <summary>Optional modules that degraded to failed.</summary>
    public IReadOnlyDictionary<string, string> FailedModules => _failedModules;

    /// <summary>Dependency-sorted init order.</summary>
    public IReadOnlyList<Type> TopologicalOrder => _topologicalOrder;

    /// <summary>Reverse shutdown order.</summary>
    public IReadOnlyList<Type> ShutdownOrder => _shutdownOrder;

    /// <summary>Runs every module's service wiring.</summary>
    public void ConfigureServicesAll(ModuleContext ctx)
    {
        foreach (var moduleType in _topologicalOrder)
        {
            if (DepFailed(moduleType)) continue;
            var module = GetOrCreateModule(moduleType);
            try { module.ConfigureServices(ctx); }
            catch (Exception ex) when (IsOptional(moduleType)) { MarkFailed(moduleType, ex, nameof(ConfigureServicesAll), ctx); }
        }
    }

    /// <summary>Runs all init hooks in topological order.</summary>
    [PubSoft.Core.BoundedCost("sync lifecycle bridge for sync hosts - Orleans ISiloLifecycle pattern")]
    public void OnInitializationAll(ModuleContext ctx)
        => OnInitializationAllAsync(ctx).AsTask().GetAwaiter().GetResult();

    /// <summary>Async variant of <see cref="OnInitializationAll"/>.</summary>
    public async ValueTask OnInitializationAllAsync(ModuleContext ctx)
    {
        foreach (var moduleType in _topologicalOrder)
        {
            if (DepFailed(moduleType) || _failedTypes.Contains(moduleType)) continue;
            var module = GetOrCreateModule(moduleType);
            try { await module.OnInitializationAsync(ctx).ConfigureAwait(false); }
            catch (Exception ex) when (IsOptional(moduleType)) { MarkFailed(moduleType, ex, nameof(OnInitializationAllAsync), ctx); }
        }
    }

    /// <summary>Runs all shutdown hooks in reverse order.</summary>
    [PubSoft.Core.BoundedCost("sync lifecycle bridge for sync hosts - Orleans ISiloLifecycle pattern")]
    public void OnShutdownAll(ModuleContext ctx)
        => OnShutdownAllAsync(ctx).AsTask().GetAwaiter().GetResult();

    /// <summary>Async variant of <see cref="OnShutdownAll"/>.</summary>
    public async ValueTask OnShutdownAllAsync(ModuleContext ctx)
    {
        foreach (var moduleType in _shutdownOrder)
        {
            if (_failedTypes.Contains(moduleType)) continue;
            if (_instances.TryGetValue(moduleType, out var module))
            {
                try { await module.OnShutdownAsync(ctx).ConfigureAwait(false); }
                catch (Exception ex) when (IsOptional(moduleType)) { MarkFailed(moduleType, ex, nameof(OnShutdownAllAsync), ctx); }
            }
        }
    }

    /// <summary>Re-runs one module's init; reports failure instead of throwing.</summary>
    [PubSoft.Core.BoundedCost("sync lifecycle bridge for sync hosts - Orleans ISiloLifecycle pattern")]
    public bool TryReinitModule(string name, ModuleContext ctx, out string? error)
    {
        var result = TryReinitModuleAsync(name, ctx).AsTask().GetAwaiter().GetResult();
        error = result.Error;
        return result.Ok;
    }

    /// <summary>Async variant of <see cref="TryReinitModule"/>.</summary>
    public async ValueTask<(bool Ok, string? Error)> TryReinitModuleAsync(string name, ModuleContext ctx)
    {
        var desc = _modules.FirstOrDefault(m => string.Equals(m.Name, name, System.StringComparison.Ordinal));
        if (desc is null)
            return (false, $"unknown module: {name}");
        if (!desc.Optional) return (false, $"module {name} is critical - restart requires process recycle");
        if (!_failedTypes.Contains(desc.ModuleType)) return (false, $"module {name} is not in failed state");
        if (DepFailed(desc.ModuleType)) return (false, $"dependency still failed: {_failedModules[desc.Name]}");

        if (_instances.TryGetValue(desc.ModuleType, out var inst))
        {
            try { await inst.OnShutdownAsync(ctx).ConfigureAwait(false); }
            catch (Exception se) { ctx.Logger?.LogWarning(se, "module {Module} shutdown during reinit", desc.Name); }
        }
        inst ??= GetOrCreateModule(desc.ModuleType);
        _failedTypes.Remove(desc.ModuleType);
        _failedModules.Remove(desc.Name);
        try
        {
            inst.ConfigureServices(ctx);
            await inst.OnInitializationAsync(ctx).ConfigureAwait(false);
            ctx.Logger?.LogInformation("module {Module} reinitialized in place", desc.Name);
            foreach (var dependent in _modules.Where(m =>
                m.Optional && _failedTypes.Contains(m.ModuleType) && (m.Dependencies?.Contains(desc.ModuleType) ?? false)))
                await TryReinitModuleAsync(dependent.Name, ctx).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            _failedTypes.Add(desc.ModuleType);
            _failedModules[desc.Name] = $"reinit: {ex.GetType().Name}: {ex.Message}";
            ctx.Logger?.LogWarning(ex, "module {Module} reinit failed", desc.Name);
            return (false, ex.Message);
        }
    }

    /// <summary>Finds a loaded module by type.</summary>
    public IPubSoftModule? GetModule(Type moduleType)
        => _instances.TryGetValue(moduleType, out var m) ? m : null;

    /// <summary>Finds a loaded module by type.</summary>
    public T? GetModule<T>() where T : class, IPubSoftModule
        => _instances.Values.OfType<T>().FirstOrDefault();

    private bool IsOptional(Type t) => _byType.TryGetValue(t, out var d) && d.Optional;

    private bool DepFailed(Type t)
    {
        if (!_byType.TryGetValue(t, out var desc)) return false;
        var bad = desc.Dependencies?.FirstOrDefault(d => _failedTypes.Contains(d));
        if (bad is null) return false;
        _failedTypes.Add(t);
        _failedModules[desc.Name] = $"dependency {bad.Name} failed";
        return true;
    }

    private void MarkFailed(Type t, Exception ex, string phase, ModuleContext ctx)
    {
        var name = _byType.TryGetValue(t, out var d) ? d.Name : t.Name;
        _failedTypes.Add(t);
        _failedModules[name] = $"{phase}: {ex.GetType().Name}: {ex.Message}";
        ctx.Logger?.LogWarning(ex, "module {Module} failed at {Phase} - degraded, service continues", name, phase);
    }

    private IPubSoftModule GetOrCreateModule(Type moduleType)
    {
        if (_instances.TryGetValue(moduleType, out var existing))
            return existing;
        var module = _moduleFactory(moduleType);
        _instances[moduleType] = module;
        return module;
    }

    private static Type[] TopologicalSort(ModuleDescriptor[] modules)
    {
        var byType = modules.ToDictionary(m => m.ModuleType);
        var inDegree = modules.ToDictionary(m => m.ModuleType, _ => 0);
        var adj = modules.ToDictionary(m => m.ModuleType, _ => new List<Type>());

        foreach (var m in modules)
        {
            foreach (var dep in m.Dependencies ?? Array.Empty<Type>())
            {
                if (byType.ContainsKey(dep))
                {
                    adj[dep].Add(m.ModuleType);
                    inDegree[m.ModuleType]++;
                }
            }
        }

        var queue = new Queue<Type>();
        foreach (var kv in inDegree)
            if (kv.Value == 0) queue.Enqueue(kv.Key);

        var result = new List<Type>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var neighbor in adj[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (result.Count != modules.Length)
            throw new InvalidOperationException("Circular module dependency detected.");

        return result.ToArray();
    }
}
