using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DataMaskingTests
{
    [Fact]
    public void MaskMethods_ApplyDefaultsAcrossCommonPii()
    {
        var masking = new DataMaskingService();

        Assert.Equal("al***@example.com", masking.MaskEmail("alice@example.com"));
        Assert.Equal("********9012", masking.MaskPhoneNumber("123456789012"));
        Assert.Equal($"123{new string('*', 12)}6789", masking.MaskIdCard("1234567890123456789"));
        Assert.Equal($"{new string('*', 14)}5678", masking.MaskBankCard(" 1234 5678 9012 3456 78 "));
        Assert.Equal("张*", masking.MaskName("张三"));
        Assert.Equal("北京市朝阳区***", masking.MaskAddress("北京市朝阳区幸福路"));
        Assert.Equal("ab******yz", masking.MaskGeneric("abcdefghyz"));
        Assert.Equal("*********", masking.MaskComplete("sensitive"));
    }

    [Fact]
    public void MaskMethods_FullyMaskWhenVisibleLengthConsumesInput()
    {
        var masking = new DataMaskingService(new DataMaskingOptions
        {
            PhoneSuffixVisibleDigits = 4,
            BankCardSuffixVisibleDigits = 4,
            IdCardPrefixVisibleDigits = 2,
            IdCardSuffixVisibleDigits = 2
        });

        Assert.Equal("****", masking.MaskPhoneNumber("1234"));
        Assert.Equal("****", masking.MaskBankCard("1234"));
        Assert.Equal("****", masking.MaskIdCard("1234"));
    }

    [Fact]
    public void MaskOptions_AreSnapshottedAtConstruction()
    {
        var options = new DataMaskingOptions { PhoneSuffixVisibleDigits = 2 };
        var masking = new DataMaskingService(options);
        options.PhoneSuffixVisibleDigits = 8;

        Assert.Equal("****56", masking.MaskPhoneNumber("123456"));
    }

    [Fact]
    public void ZeroVisibleLengthMasksTheWholeValue()
    {
        var masking = new DataMaskingService(new DataMaskingOptions
        {
            EmailPrefixVisibleChars = 0,
            PhoneSuffixVisibleDigits = 0,
            IdCardPrefixVisibleDigits = 0,
            IdCardSuffixVisibleDigits = 0,
            BankCardSuffixVisibleDigits = 0
        });

        Assert.Equal("*****@example.com", masking.MaskEmail("alice@example.com"));
        Assert.Equal("******", masking.MaskPhoneNumber("123456"));
        Assert.Equal("******", masking.MaskIdCard("123456"));
        Assert.Equal("******", masking.MaskBankCard("123456"));
    }

    [Fact]
    public void MaskMethods_ReturnEmptyForNullOrWhitespace()
    {
        var masking = new DataMaskingService();

        Assert.Equal(string.Empty, masking.MaskEmail(null));
        Assert.Equal(string.Empty, masking.MaskPhoneNumber("  "));
        Assert.Equal(string.Empty, masking.MaskIdCard(null));
        Assert.Equal(string.Empty, masking.MaskBankCard("\t"));
        Assert.Equal(string.Empty, masking.MaskName(null));
        Assert.Equal(string.Empty, masking.MaskAddress(" "));
        Assert.Equal(string.Empty, masking.MaskGeneric(null));
        Assert.Equal(string.Empty, masking.MaskComplete("\r\n"));
    }

    [Fact]
    public void Constructor_RejectsNegativeVisibleLengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMaskingService(new DataMaskingOptions { EmailPrefixVisibleChars = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMaskingService(new DataMaskingOptions { PhoneSuffixVisibleDigits = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMaskingService(new DataMaskingOptions { IdCardPrefixVisibleDigits = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMaskingService(new DataMaskingOptions { IdCardSuffixVisibleDigits = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMaskingService(new DataMaskingOptions { BankCardSuffixVisibleDigits = -1 }));
    }

    [Fact]
    public void MaskGeneric_RejectsNegativeVisibleLengths()
    {
        var masking = new DataMaskingService();

        Assert.Throws<ArgumentOutOfRangeException>(() => masking.MaskGeneric("sensitive", -1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => masking.MaskGeneric("sensitive", 2, -1));
    }
}