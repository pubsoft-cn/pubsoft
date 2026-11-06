using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


public static class HmacValidationServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftHmacValidation(
        this IServiceCollection services,
        Action<HmacValidationOptions>? configure = null,
        Action<HmacSignatureOptions>? configureSignature = null)
    {
        services.TryAddSingleton<INonceStore>(sp =>
        {
            sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                ?.CreateLogger("PubSoft.AspNetCore.HmacValidation")
                .LogWarning(
                    "INonceStore fell back to process-local MemoryNonceStore - replay " +
                    "protection is per-instance; register a shared INonceStore " +
                    "(e.g. Redis) when running multiple replicas.");
            return new MemoryNonceStore();
        });
        services.AddSingleton(sp =>
        {
            var options = new HmacSignatureOptions();
            configureSignature?.Invoke(options);
            return new HmacSignatureValidator(sp.GetRequiredService<INonceStore>(), options);
        });
        services.Configure(configure ?? (_ => { }));
        return services;
    }

    public static IApplicationBuilder UsePubSoftHmacValidation(this IApplicationBuilder app)
        => app.UseMiddleware<HmacValidationMiddleware>();
}
