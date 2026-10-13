using Microsoft.AspNetCore.Http;
using PubSoft.Authentication;

namespace PubSoft.AspNetCore.Authentication;

internal sealed class HttpContextPrincipalSnapshotAccessor(IHttpContextAccessor httpContextAccessor) : IPrincipalSnapshotAccessor
{
    public PrincipalSnapshot? GetCurrent()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal is null ? null : PrincipalSnapshot.FromClaimsPrincipal(principal);
    }
}