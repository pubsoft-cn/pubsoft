using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


public sealed class PubSoftRateLimitPolicy
{
    private readonly Func<HttpContext, bool> _appliesTo;
    private readonly Func<HttpContext, string?> _identifierSelector;
    private readonly Func<HttpContext, int, bool> _isFailureResponse;
    private readonly RateLimiter _limiter;

    public PubSoftRateLimitPolicy(PubSoftRateLimitPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        if (!string.Equals(options.Name, options.Name.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Policy names must have no surrounding whitespace.", nameof(options));

        Name = options.Name;
        CountFailuresOnly = options.CountFailuresOnly;
        _appliesTo = options.AppliesTo ?? throw new ArgumentException("A policy matcher is required.", nameof(options));
        _identifierSelector = options.IdentifierSelector ?? throw new ArgumentException("An identifier selector is required.", nameof(options));
        _isFailureResponse = options.IsFailureResponse ?? throw new ArgumentException("A failure response selector is required.", nameof(options));
        _limiter = new RateLimiter(new RateLimiterOptions
        {
            RequestsPerWindow = options.RequestsPerWindow,
            WindowSize = options.WindowSize,
            MaxIdentifiers = options.MaxIdentifiers
        });
    }

    public string Name { get; }
    public bool CountFailuresOnly { get; }
    internal bool Applies(HttpContext context) => _appliesTo(context);
    internal string? GetIdentifier(HttpContext context) => _identifierSelector(context);
    internal bool IsFailure(HttpContext context, int statusCode) => _isFailureResponse(context, statusCode);
    internal RateLimitResult Check(string? identifier, bool countRequest) => _limiter.CheckRateLimit(identifier, countRequest);
}
