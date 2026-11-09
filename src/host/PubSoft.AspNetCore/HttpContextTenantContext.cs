using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;


/// <summary>Tenant context resolved from ambient scope, then opt-in request header, then claims.</summary>
public sealed class HttpContextTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HttpContextTenantOptions _options;
    private readonly ITenantContext _ambient;

    /// <summary>Creates the resolver.</summary>
    public HttpContextTenantContext(IHttpContextAccessor httpContextAccessor,
        HttpContextTenantOptions? options = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options ?? new HttpContextTenantOptions();
        _ambient = new AmbientTenantContext();
    }

    /// <inheritdoc />
    public long? TenantId
    {
        get
        {
            var ambient = _ambient.TenantId;
            if (ambient.HasValue) return ambient;
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null) return null;
            if (_options.ReadHeader)
            {
                var headerValue = httpContext.Request.Headers[_options.HeaderName].FirstOrDefault();
                if (long.TryParse(headerValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var headerId)) return headerId;
            }
            foreach (var claimName in _options.ClaimNames)
            {
                var claim = httpContext.User?.FindFirst(claimName);
                if (claim != null && long.TryParse(claim.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var claimId)) return claimId;
            }
            return null;
        }
    }

    /// <inheritdoc />
    public IDisposable Change(long? tenantId) => _ambient is AmbientTenantContext a ? a.Change(tenantId) : new NoopScope();

    private sealed class NoopScope : IDisposable { public void Dispose() { } }
}
