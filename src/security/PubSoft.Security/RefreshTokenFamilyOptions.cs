namespace PubSoft.Security;

/// <summary>Bounded options for refresh-token family rotation.</summary>
public sealed class RefreshTokenFamilyOptions
{
    /// <summary>Lifetime of each individual refresh token; &gt;0 and &lt;=30 days.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Absolute lifetime of a family counted from its first issue; must be >=
    /// <see cref="TokenLifetime"/> and &lt;=90 days.
    /// </summary>
    public TimeSpan FamilyMaxLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Maximum accepted token string length; 32-512.</summary>
    public int MaxTokenLength { get; set; } = 256;

    /// <summary>Maximum subject length; 1-1024.</summary>
    public int MaxSubjectLength { get; set; } = 256;

    internal void Validate()
    {
        if (TokenLifetime <= TimeSpan.Zero || TokenLifetime > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(TokenLifetime), TokenLifetime, "TokenLifetime must be >0 and <=30 days.");
        if (FamilyMaxLifetime <= TimeSpan.Zero || FamilyMaxLifetime > TimeSpan.FromDays(90))
            throw new ArgumentOutOfRangeException(nameof(FamilyMaxLifetime), FamilyMaxLifetime, "FamilyMaxLifetime must be >0 and <=90 days.");
        if (FamilyMaxLifetime < TokenLifetime)
            throw new ArgumentException("FamilyMaxLifetime must be >= TokenLifetime.", nameof(FamilyMaxLifetime));
        if (MaxTokenLength is < 32 or > 512)
            throw new ArgumentOutOfRangeException(nameof(MaxTokenLength), MaxTokenLength, "MaxTokenLength must be between 32 and 512.");
        if (MaxSubjectLength is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxSubjectLength), MaxSubjectLength, "MaxSubjectLength must be between 1 and 1024.");
    }
}
