using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.EntityFrameworkCore;

/// <summary>DbContext options hooks for query diagnostics.</summary>
public static class DbContextOptionsBuilderDiagnosticsExtensions
{
    /// <summary>
    /// Attaches the registered <see cref="SlowQueryInterceptor"/> to this context:
    /// <c>AddDbContext&lt;T&gt;((sp, o) =&gt; o.UseXxx(...).UsePubSoftQueryDiagnostics(sp))</c>.
    /// Throws when <see cref="DbQueryDiagnosticsServiceCollectionExtensions.AddPubSoftDbQueryDiagnostics"/>
    /// was not registered (or ran with <c>Enabled=false</c>) - a silent no-op here
    /// would recreate the half-wired state this hook exists to remove.
    /// </summary>
    public static DbContextOptionsBuilder UsePubSoftQueryDiagnostics(
        this DbContextOptionsBuilder builder, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var interceptor = serviceProvider.GetService<SlowQueryInterceptor>()
            ?? throw new InvalidOperationException(
                "SlowQueryInterceptor is not registered - call services.AddPubSoftDbQueryDiagnostics() first.");
        return builder.AddInterceptors(interceptor);
    }
}
