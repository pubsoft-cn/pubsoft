namespace PubSoft.OAuth;


/// <summary>Built-in provider name constants.</summary>
public static class OAuthProviderNames
{
    public const string WeChat = "WeChat";
    public const string WeCom = "WeCom";
    public const string Alipay = "Alipay";
    public const string DingTalk = "DingTalk";
    public const string QQ = "QQ";
    public const string Douyin = "Douyin";
    public const string Toutiao = "Toutiao";
    public const string Taobao = "Taobao";
    public const string Huawei = "Huawei";
    public const string Xiaomi = "Xiaomi";
    public const string GitHub = "GitHub";
    public const string Gitee = "Gitee";
    public const string Baidu = "Baidu";
    public const string Weibo = "Weibo";
    public const string Apple = "Apple";
    public const string Microsoft = "Microsoft";
    public const string Google = "Google";
    public const string GitLab = "GitLab";
    public const string Twitter = "Twitter";
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
