using System.Text.Json;
using PubSoft.Edge;

// Signer round-trip + requestId binding + canonical-JSON key sorting.
var p = JsonDocument.Parse("{\"itemId\":42}").RootElement;
var sign = EdgeSigner.SignHmac("demo.ping", 1758000000, "n-1", "req-1", p, "key");
if (!EdgeSigner.VerifyHmac("demo.ping", 1758000000, "n-1", "req-1", p, "key", sign)
    || EdgeSigner.VerifyHmac("demo.ping", 1758000000, "n-1", "req-forged", p, "key", sign))
    throw new InvalidOperationException("Edge signer consumer check failed.");

var md5 = EdgeSigner.SignMd5("demo.ping", 1758000000, "n-1", "req-1", p, "key");
if (!EdgeSigner.VerifyMd5("demo.ping", 1758000000, "n-1", "req-1", p, "key", md5)
    || EdgeSigner.VerifyMd5("demo.ping", 1758000000, "n-1", "req-1", p, "other", md5))
    throw new InvalidOperationException("Edge MD5 scheme consumer check failed.");

var req = new EdgeRpcRequest
{
    ClientId = "demo-app",
    Method = "demo.ping",
    Scheme = EdgeAuthScheme.Hmac,
    Credential = sign,
    Timestamp = 1758000000,
    Nonce = "n-1",
    RequestId = "req-1",
    Params = p,
    Version = "1.0",
};
var result = EdgeSigner.Verify(req, "key");
if (!result.Success)
    throw new InvalidOperationException($"Envelope verify failed: {result.ErrorMessage}");

Console.WriteLine("Isolated PubSoft.Edge.Core consumer smoke check passed.");
