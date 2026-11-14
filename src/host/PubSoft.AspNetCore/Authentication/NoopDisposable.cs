namespace PubSoft.AspNetCore.Authentication;


internal sealed class NoopDisposable : IDisposable
{
    internal static readonly NoopDisposable Instance = new();
    public void Dispose() { }
}
