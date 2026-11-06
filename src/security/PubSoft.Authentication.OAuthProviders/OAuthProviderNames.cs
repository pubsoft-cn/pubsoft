namespace PubSoft.OAuth;


/// <summary>Built-in provider name constants.</summary>
public static class OAuthProviderNames
{
    /// <summary>WeChat.</summary>
    public const string WeChat = "WeChat";
    /// <summary>WeCom.</summary>
    public const string WeCom = "WeCom";
    /// <summary>Alipay.</summary>
    public const string Alipay = "Alipay";
    /// <summary>DingTalk.</summary>
    public const string DingTalk = "DingTalk";
    /// <summary>QQ.</summary>
    public const string QQ = "QQ";
    /// <summary>Douyin.</summary>
    public const string Douyin = "Douyin";
    /// <summary>Toutiao.</summary>
    public const string Toutiao = "Toutiao";
    /// <summary>Taobao.</summary>
    public const string Taobao = "Taobao";
    /// <summary>Huawei.</summary>
    public const string Huawei = "Huawei";
    /// <summary>Xiaomi.</summary>
    public const string Xiaomi = "Xiaomi";
    /// <summary>GitHub.</summary>
    public const string GitHub = "GitHub";
    /// <summary>Gitee.</summary>
    public const string Gitee = "Gitee";
    /// <summary>Baidu.</summary>
    public const string Baidu = "Baidu";
    /// <summary>Weibo.</summary>
    public const string Weibo = "Weibo";
    /// <summary>Apple.</summary>
    public const string Apple = "Apple";
    /// <summary>Microsoft.</summary>
    public const string Microsoft = "Microsoft";
    /// <summary>Google.</summary>
    public const string Google = "Google";
    /// <summary>GitLab.</summary>
    public const string GitLab = "GitLab";
    /// <summary>Twitter.</summary>
    public const string Twitter = "Twitter";
    /// <summary>A custom provider supplied by the host.</summary>
    public const string Custom = "Custom";

    /// <summary>All built-in providers.</summary>
    public static readonly string[] BuiltIn =
    [
        WeChat, WeCom, Alipay, DingTalk, QQ,
        Douyin, Toutiao, Taobao, Huawei, Xiaomi,
        GitHub, Gitee, Baidu, Weibo, Apple,
        Microsoft, Google, GitLab, Twitter,
    ];

    /// <summary>Whether the name is a known built-in provider (case-insensitive).</summary>
    public static bool IsKnown(string provider) =>
        BuiltIn.Any(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
}
