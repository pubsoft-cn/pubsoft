using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PubSoft.Authentication;

namespace PubSoft.AspNetCore;

/// <summary>
/// Lightweight Controller base -- extends the stock ASP.NET Core <see cref="ControllerBase"/>. 
/// <para>Resolves <see cref="IAuthenticatedSubjectAccessor"/> from DI (null when unregistered = anonymous).</para>
/// </summary>
[ApiController]
public abstract class PubSoftController : ControllerBase
{

    /// <summary>Creates the controller with the subject accessor.</summary>
    protected PubSoftController(IAuthenticatedSubjectAccessor? subjectAccessor)
        => SubjectAccessor = subjectAccessor;
    /// <summary>The authenticated subject resolved from DI (may be anonymous).</summary>
    protected IAuthenticatedSubjectAccessor? SubjectAccessor { get; }
}

/// <summary>
/// Dependency-free Controller base -- for controllers that only need a Logger and no subject context. 
/// </summary>
[ApiController]
public abstract class PubSoftControllerBase : ControllerBase
{
    private ILogger? _logger;

    /// <summary>Logger -- resolves lazily from HttpContext.RequestServices, falls back to NullLogger.</summary>
    protected ILogger Logger =>
        _logger ??= HttpContext?.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(GetType())
                    ?? NullLogger.Instance;
}
