using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>Options for <see cref="JwtClientAssertionVerifier"/>.</summary>
public sealed class ClientAssertionOptions
{
    /// <summary>
    /// Audience the assertion must target -- this service's identifier. Required; assertions minted
    /// for other audiences are rejected.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>Maximum accepted assertion lifetime (exp - iat); default 5 minutes, bounded to 15.</summary>
    public TimeSpan MaxAssertionLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Clock skew applied to lifetime checks; default 30 seconds, bounded to 5 minutes.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Audience);
        if (Audience.Length > 256 || Audience.Any(char.IsControl))
            throw new ArgumentException("Audience must be at most 256 non-control characters.", nameof(Audience));
        if (MaxAssertionLifetime <= TimeSpan.Zero || MaxAssertionLifetime > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(MaxAssertionLifetime), "MaxAssertionLifetime must be positive and no longer than 15 minutes.");
        if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(ClockSkew), "ClockSkew must be between zero and five minutes.");
    }
}
