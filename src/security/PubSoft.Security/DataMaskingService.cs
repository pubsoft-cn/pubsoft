namespace PubSoft.Security;


/// <summary>Masks common personally identifiable information for logs and responses.</summary>
public sealed class DataMaskingService
{
    private readonly DataMaskingOptions _options;

    /// <summary>Creates a masking service; null options use defaults.</summary>
    public DataMaskingService(DataMaskingOptions? options = null)
    {
        options ??= new DataMaskingOptions();
        Validate(options);
        _options = Copy(options);
    }

    /// <summary>Masks the local part of an email address, keeping the domain visible.</summary>
    public string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        email = email.Trim();
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return new string(_options.EmailMaskChar, Math.Min(email.Length, 4));

        var username = email[..atIndex];
        var domain = email[(atIndex + 1)..];
        var visiblePrefixLength = _options.EmailPrefixVisibleChars;
        if (username.Length <= visiblePrefixLength)
            return $"{new string(_options.EmailMaskChar, username.Length)}@{domain}";

        return $"{username[..visiblePrefixLength]}{new string(_options.EmailMaskChar, username.Length - visiblePrefixLength)}@{domain}";
    }

    /// <summary>Masks a phone number, keeping the configured number of trailing digits.</summary>
    public string MaskPhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        phone = phone.Trim();
        var visibleSuffixLength = _options.PhoneSuffixVisibleDigits;
        if (phone.Length <= visibleSuffixLength)
            return new string(_options.PhoneMaskChar, phone.Length);

        return $"{new string(_options.PhoneMaskChar, phone.Length - visibleSuffixLength)}{phone[^visibleSuffixLength..]}";
    }

    /// <summary>Masks an ID card number, keeping configured leading and trailing digits.</summary>
    public string MaskIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return string.Empty;

        idCard = idCard.Trim();
        var prefixLength = _options.IdCardPrefixVisibleDigits;
        var suffixLength = _options.IdCardSuffixVisibleDigits;
        if (prefixLength >= idCard.Length || suffixLength >= idCard.Length - prefixLength)
            return new string(_options.IdCardMaskChar, Math.Min(idCard.Length, 6));

        return $"{idCard[..prefixLength]}{new string(_options.IdCardMaskChar, idCard.Length - prefixLength - suffixLength)}{idCard[^suffixLength..]}";
    }

    /// <summary>Masks a bank card number, keeping configured trailing digits.</summary>
    public string MaskBankCard(string? bankCard)
    {
        if (string.IsNullOrWhiteSpace(bankCard))
            return string.Empty;

        bankCard = bankCard.Trim().Replace(" ", string.Empty, StringComparison.Ordinal); // pubsoft:exempt PUBSOFT022 "char overload cannot delete characters"
        var visibleSuffixLength = _options.BankCardSuffixVisibleDigits;
        if (bankCard.Length <= visibleSuffixLength)
            return new string(_options.BankCardMaskChar, bankCard.Length);

        return $"{new string(_options.BankCardMaskChar, bankCard.Length - visibleSuffixLength)}{bankCard[^visibleSuffixLength..]}";
    }

    /// <summary>Masks a person name, keeping first and last character.</summary>
    public string MaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Trim();
        if (name.Length <= 1)
            return new string(_options.GeneralMaskChar, name.Length);
        if (name.Length == 2)
            return $"{name[0]}{_options.GeneralMaskChar}";

        return $"{name[0]}{new string(_options.GeneralMaskChar, name.Length - 2)}{name[^1]}";
    }

    /// <summary>Masks an address, keeping a short prefix for region context.</summary>
    public string MaskAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        address = address.Trim();
        if (address.Length <= 6)
            return new string(_options.GeneralMaskChar, address.Length);

        return $"{address[..6]}{new string(_options.GeneralMaskChar, address.Length - 6)}";
    }

    /// <summary>Masks an arbitrary value, keeping <paramref name="prefixLength"/> leading and <paramref name="suffixLength"/> trailing characters.</summary>
    public string MaskGeneric(string? value, int prefixLength = 2, int suffixLength = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        ArgumentOutOfRangeException.ThrowIfNegative(suffixLength);

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        if (prefixLength >= value.Length || suffixLength >= value.Length - prefixLength)
            return new string(_options.GeneralMaskChar, value.Length);

        return $"{value[..prefixLength]}{new string(_options.GeneralMaskChar, value.Length - prefixLength - suffixLength)}{value[^suffixLength..]}";
    }

    /// <summary>Masks a value entirely.</summary>
    public string MaskComplete(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(_options.GeneralMaskChar, value.Length);
    }

    private static void Validate(DataMaskingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.EmailPrefixVisibleChars);
        ArgumentOutOfRangeException.ThrowIfNegative(options.PhoneSuffixVisibleDigits);
        ArgumentOutOfRangeException.ThrowIfNegative(options.IdCardPrefixVisibleDigits);
        ArgumentOutOfRangeException.ThrowIfNegative(options.IdCardSuffixVisibleDigits);
        ArgumentOutOfRangeException.ThrowIfNegative(options.BankCardSuffixVisibleDigits);
    }

    private static DataMaskingOptions Copy(DataMaskingOptions options) => new()
    {
        EmailMaskChar = options.EmailMaskChar,
        PhoneMaskChar = options.PhoneMaskChar,
        IdCardMaskChar = options.IdCardMaskChar,
        BankCardMaskChar = options.BankCardMaskChar,
        GeneralMaskChar = options.GeneralMaskChar,
        EmailPrefixVisibleChars = options.EmailPrefixVisibleChars,
        PhoneSuffixVisibleDigits = options.PhoneSuffixVisibleDigits,
        IdCardPrefixVisibleDigits = options.IdCardPrefixVisibleDigits,
        IdCardSuffixVisibleDigits = options.IdCardSuffixVisibleDigits,
        BankCardSuffixVisibleDigits = options.BankCardSuffixVisibleDigits
    };
}
