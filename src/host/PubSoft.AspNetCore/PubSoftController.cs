using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.Authentication;

namespace PubSoft.AspNetCore;

/// <summary>
/// 轻量 Controller 基类 — 继承 ASP.NET Core 原生 <see cref="ControllerBase"/>。
/// <para>通过 DI 注入 <see cref="IAuthenticatedSubjectAccessor"/>（未注册时 = null，匿名）。</para>
/// </summary>
[ApiController]
public abstract class PubSoftController : ControllerBase
{

    protected PubSoftController(IAuthenticatedSubjectAccessor? subjectAccessor)
        => SubjectAccessor = subjectAccessor;
    /// <summary>当前认证主体（DI 注入，可能为匿名）。</summary>
    protected IAuthenticatedSubjectAccessor? SubjectAccessor { get; }
}

/// <summary>
/// 无注入 Controller 基类 — 不需要 subject 上下文的 Controller 用此基类（仅提供 Logger）。
/// </summary>
[ApiController]
public abstract class PubSoftControllerBase : ControllerBase
{
    private ILogger? _logger;

    /// <summary>日志记录器 — 从 HttpContext.RequestServices 惰性解析，兜底 NullLogger。</summary>
    protected ILogger Logger =>
        _logger ??= HttpContext?.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(GetType())
                    ?? NullLogger.Instance;
}
