using PubSoft.Notifications;

var channel = new SmokeChannel();
var dispatcher = new NotificationDispatcher([channel]);
var message = new NotificationMessage("package-restored", correlationId: "smoke-1");
var first = await dispatcher.DispatchAsync("smoke", message);
var duplicate = await dispatcher.DispatchAsync("smoke", message);

if (!first.Success || duplicate.ProviderMessageId != "suppressed" || channel.SendCount != 1)
    throw new InvalidOperationException("The isolated PubSoft.Notifications package smoke check failed.");

Console.WriteLine("Isolated PubSoft.Notifications consumer smoke check passed.");

sealed class SmokeChannel : INotificationChannel
{
    public string Name => "smoke";
    public int SendCount { get; private set; }

    public Task<NotificationResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendCount++;
        return Task.FromResult(NotificationResult.Ok("provider-1"));
    }
}