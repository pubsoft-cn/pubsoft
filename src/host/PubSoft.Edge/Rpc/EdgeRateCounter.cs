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
