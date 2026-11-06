namespace PubSoft.Security;

/// <summary>Configures masking characters and the number of visible characters.</summary>
public sealed class DataMaskingOptions
{
    public char EmailMaskChar { get; set; } = '*';
    public char PhoneMaskChar { get; set; } = '*';
    public char IdCardMaskChar { get; set; } = '*';
    public char BankCardMaskChar { get; set; } = '*';
    public char GeneralMaskChar { get; set; } = '*';
    public int EmailPrefixVisibleChars { get; set; } = 2;
    public int PhoneSuffixVisibleDigits { get; set; } = 4;
    public int IdCardPrefixVisibleDigits { get; set; } = 3;
    public int IdCardSuffixVisibleDigits { get; set; } = 4;
    public int BankCardSuffixVisibleDigits { get; set; } = 4;
}
