using PubSoft.Domain;

namespace PubSoft.Identity;

/// <summary>
/// External-login provider binding for an <see cref="IdentityUser"/> -- the row linking a
/// user to a third-party identity (WeChat/Alipay/...). (LoginProvider, ProviderKey) is the
/// unique lookup pair; one user may hold many bindings.
/// </summary>
public sealed class IdentityUserLogin : FullAuditedAggregateRoot
{
    private const int MaxProviderLength = 64;
    private const int MaxProviderKeyLength = 256;
    private const int MaxDisplayNameLength = 128;

    /// <summary>Creates a provider binding for a user - (loginProvider, providerKey) is the unique lookup pair.</summary>
    public IdentityUserLogin(
        long id, long userId, string loginProvider, string providerKey,
        string? providerDisplayName = null, long? tenantId = null)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Login ID must be positive.");
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "User ID must be positive.");
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId), "Tenant ID must be positive when specified.");

        Id = id;
        UserId = userId;
        TenantId = tenantId;
        SetProvider(loginProvider, providerKey);
        ProviderDisplayName = providerDisplayName;
    }

    private IdentityUserLogin()
    {
        UserId = 0;
        LoginProvider = string.Empty;
        ProviderKey = string.Empty;
    }

    /// <summary>The user this binding belongs to.</summary>
    public long UserId { get; }
    /// <summary>Provider identifier (e.g. "wechat", "alipay") - keys the OAuth provider registry.</summary>
    public string LoginProvider { get; private set; }
    /// <summary>The user's unique id at the provider (openid/unionid).</summary>
    public string ProviderKey { get; private set; }
    /// <summary>Friendly provider-side name for UI.</summary>
    public string? ProviderDisplayName { get; private set; }

    /// <summary>Opaque provider-side payload (unionid, nickname, ...) -- the entity validates nothing about its shape.</summary>
    public string? ExtJson { get; set; }

    /// <summary>Updates the friendly provider-side name.</summary>
    public void SetProviderDisplayName(string? displayName)
    {
        if (displayName is { Length: > MaxDisplayNameLength })
            throw new ArgumentException($"Provider display names cannot exceed {MaxDisplayNameLength} characters.", nameof(displayName));
        ProviderDisplayName = displayName;
    }

    private void SetProvider(string loginProvider, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        if (loginProvider.Length > MaxProviderLength)
            throw new ArgumentException($"Login providers cannot exceed {MaxProviderLength} characters.", nameof(loginProvider));
        if (providerKey.Length > MaxProviderKeyLength)
            throw new ArgumentException($"Provider keys cannot exceed {MaxProviderKeyLength} characters.", nameof(providerKey));
        LoginProvider = loginProvider;
        ProviderKey = providerKey;
    }
}
