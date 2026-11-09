using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;


/// <summary>
/// Rate counter.
/// </summary>
internal sealed class EdgeRateCounter
{
    public int Count;
    public DateTime ResetAt { get; set; }

    public bool IsExceeded(int limit) => Count >= limit;

    public EdgeRateCounter Increment() => new() { Count = Count + 1, ResetAt = ResetAt };
}
