using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;


/// <summary>Result of a WeChat global access_token fetch (cgi-bin/token).</summary>
public sealed class WeChatAccessTokenResult
{
    public bool Success { get; set; }
    public string AccessToken { get; set; } = "";
    public string? Message { get; set; }

    public static WeChatAccessTokenResult Ok(string token) => new() { Success = true, AccessToken = token };
    public static WeChatAccessTokenResult Fail(string msg) => new() { Success = false, Message = msg };
}
