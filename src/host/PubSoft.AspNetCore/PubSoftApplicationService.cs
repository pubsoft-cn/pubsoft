using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.DependencyInjection;

namespace PubSoft.AspNetCore;

/// <summary>
/// 轻量 ApplicationService 基类 — 实现 <see cref="ITransientDependency"/> 供约定注册。
/// <para>无 ORM/映射/本地化依赖 — 映射层调用方自行注入；<see cref="Logger"/> 惰性从 <see cref="IServiceProvider"/> 解析。</para>
/// </summary>
public abstract class PubSoftApplicationService : ITransientDependency
{
    private readonly IServiceProvider? _serviceProvider;
    private ILogger? _logger;

    /// <summary>日志记录器 — 优先 DI 注入 IServiceProvider 解析，兜底 NullLogger。</summary>
    protected ILogger Logger =>
        _logger ??= _serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger(GetType())
                    ?? NullLogger.Instance;

    /// <summary>主构造 — 供 ActivatorUtilities 注入 IServiceProvider 以支撑 Logger 惰性解析。</summary>
    [ActivatorUtilitiesConstructor]
    protected PubSoftApplicationService(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    protected PubSoftApplicationService() { }
}
