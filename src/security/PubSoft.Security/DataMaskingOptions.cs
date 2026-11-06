namespace PubSoft.Security;

/// <summary>Configures masking characters and the number of visible characters.</summary>
public sealed class DataMaskingOptions
{
    /// <summary>Replacement character for email masking.</summary>
    public char EmailMaskChar { get; set; } = '*';
    /// <summary>Replacement character for phone masking.</summary>
    public char PhoneMaskChar { get; set; } = '*';
    /// <summary>Replacement character for ID card masking.</summary>
    public char IdCardMaskChar { get; set; } = '*';
    /// <summary>Replacement character for bank card masking.</summary>
    public char BankCardMaskChar { get; set; } = '*';
    /// <summary>Replacement character for generic masking.</summary>
    public char GeneralMaskChar { get; set; } = '*';
    /// <summary>Local-part characters kept visible before the mask.</summary>
    public int EmailPrefixVisibleChars { get; set; } = 2;
    /// <summary>Trailing digits kept visible in phone numbers.</summary>
    public int PhoneSuffixVisibleDigits { get; set; } = 4;
    /// <summary>Leading digits kept visible in ID cards.</summary>
    public int IdCardPrefixVisibleDigits { get; set; } = 3;
    /// <summary>Trailing digits kept visible in ID cards.</summary>
    public int IdCardSuffixVisibleDigits { get; set; } = 4;
    /// <summary>Trailing digits kept visible in bank cards.</summary>
    public int BankCardSuffixVisibleDigits { get; set; } = 4;
}
