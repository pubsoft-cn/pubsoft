using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

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

    public ModuleOrchestrator(
        ModuleDescriptor[] modules,
        Func<Type, IPubSoftModule> moduleFactory,
        Type[]? topologicalOrder = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(moduleFactory);
        var duplicateName = modules.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateName is not null)
            throw new ArgumentException($"duplicate module name: {duplicateName.Key}", nameof(modules));
        _modules = modules;
        _byType = modules.ToDictionary(m => m.ModuleType);
        _moduleFactory = moduleFactory;
        _topologicalOrder = topologicalOrder ?? TopologicalSort(modules);
        _shutdownOrder = (Type[])_topologicalOrder.Clone();
        Array.Reverse(_shutdownOrder);
    }

    public int ModuleCount => _modules.Length;

    public IReadOnlyDictionary<string, string> FailedModules => _failedModules;

    public IReadOnlyList<Type> TopologicalOrder => _topologicalOrder;

    public IReadOnlyList<Type> ShutdownOrder => _shutdownOrder;

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

    public void OnInitializationAll(ModuleContext ctx)
    {
        foreach (var moduleType in _topologicalOrder)
        {
            if (DepFailed(moduleType) || _failedTypes.Contains(moduleType)) continue;
            var module = GetOrCreateModule(moduleType);
            try { module.OnInitialization(ctx); }
            catch (Exception ex) when (IsOptional(moduleType)) { MarkFailed(moduleType, ex, nameof(OnInitializationAll), ctx); }
        }
    }

    public void OnShutdownAll(ModuleContext ctx)
    {
        foreach (var moduleType in _shutdownOrder)
        {
            if (_failedTypes.Contains(moduleType)) continue;
            if (_instances.TryGetValue(moduleType, out var module))
            {
                try { module.OnShutdown(ctx); }
                catch (Exception ex) when (IsOptional(moduleType)) { MarkFailed(moduleType, ex, nameof(OnShutdownAll), ctx); }
            }
        }
    }

    public bool TryReinitModule(string name, ModuleContext ctx, out string? error)
    {
        error = null;
        var desc = _modules.FirstOrDefault(m => m.Name == name);
        if (desc is null)
        {
            error = $"unknown module: {name}";
            return false;
        }
        if (!desc.Optional) { error = $"module {name} is critical — restart requires process recycle"; return false; }
        if (!_failedTypes.Contains(desc.ModuleType)) { error = $"module {name} is not in failed state"; return false; }
        if (DepFailed(desc.ModuleType)) { error = $"dependency still failed: {_failedModules[desc.Name]}"; return false; }

        if (_instances.TryGetValue(desc.ModuleType, out var inst))
        {
            try { inst.OnShutdown(ctx); }
            catch (Exception se) { ctx.Logger?.LogWarning(se, "module {Module} shutdown during reinit", desc.Name); }
        }
        inst ??= GetOrCreateModule(desc.ModuleType);
        _failedTypes.Remove(desc.ModuleType);
        _failedModules.Remove(desc.Name);
        try
        {
            inst.ConfigureServices(ctx);
            inst.OnInitialization(ctx);
            ctx.Logger?.LogInformation("module {Module} reinitialized in place", desc.Name);
            foreach (var dependent in _modules.Where(m =>
                m.Optional && _failedTypes.Contains(m.ModuleType) && (m.Dependencies?.Contains(desc.ModuleType) ?? false)))
                TryReinitModule(dependent.Name, ctx, out _);
            return true;
        }
        catch (Exception ex)
        {
            _failedTypes.Add(desc.ModuleType);
            _failedModules[desc.Name] = $"reinit: {ex.GetType().Name}: {ex.Message}";
            error = ex.Message;
            ctx.Logger?.LogWarning(ex, "module {Module} reinit failed", desc.Name);
            return false;
        }
    }

    public IPubSoftModule? GetModule(Type moduleType)
        => _instances.TryGetValue(moduleType, out var m) ? m : null;

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
        ctx.Logger?.LogWarning(ex, "module {Module} failed at {Phase} — degraded, service continues", name, phase);
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
