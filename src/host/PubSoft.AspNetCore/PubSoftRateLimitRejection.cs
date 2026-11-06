using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


/// <summary>
/// Customizes the 429 rejection body written by the rate-limiting middlewares.
/// Status code and Retry-After are already set by the middleware; the writer owns the body only.
/// Register once via <see cref="PubSoftRateLimitRejectionExtensions.AddPubSoftRateLimitRejection"/>;
/// default body is <c>{"error":"rate_limit_exceeded"}</c>.
/// </summary>
public sealed class PubSoftRateLimitRejection
{
    public PubSoftRateLimitRejection(Func<HttpContext, Task> bodyWriter)
    {
        BodyWriter = bodyWriter ?? throw new ArgumentNullException(nameof(bodyWriter));
    }

    /// <summary>Writes the rejection response body (status 429 + Retry-After already set).</summary>
    public Func<HttpContext, Task> BodyWriter { get; }
}
