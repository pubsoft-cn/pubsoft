using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PubSoft.Core;

namespace PubSoft.AspNetCore;

public sealed class GlobalExceptionHandlerOptions
{
    public int DomainExceptionStatusCode { get; set; } = 400;
    public int ArgumentExceptionStatusCode { get; set; } = 400;
    public int InvalidOperationExceptionStatusCode { get; set; } = 400;
    public int UnauthorizedAccessStatusCode { get; set; } = 401;
    public int KeyNotFoundStatusCode { get; set; } = 404;
    public int DefaultStatusCode { get; set; } = 500;
    public string DefaultMessage { get; set; } = "server error";
    public string UnauthorizedMessage { get; set; } = "unauthorized";
    public Dictionary<Type, int> CustomMappings { get; } = new();
}
