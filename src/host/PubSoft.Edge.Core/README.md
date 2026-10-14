# PubSoft.Edge.Core

Edge RPC primitives: request/response envelope, credential schemes, canonical signing, and the shared error-code vocabulary.

## Request envelope

```json
{
  "clientId": "demo-app",
  "method": "demo.ping",
  "scheme": "hmac",
  "credential": "BASE64-SIGNATURE",
  "timestamp": 1758700000000,
  "nonce": "a1b2c3",
  "requestId": "req-7f3a9e",
  "params": { "itemId": 123 },
  "version": "1.0"
}
```

| Field | Meaning |
|---|---|
| `clientId` | Registered calling client identifier |
| `method` | RPC method (`domain.action`) |
| `scheme` | `hmac` / `session` / `none` / `md5` |
| `credential` | Signature or sessionKey, per scheme |
| `timestamp` | Unix epoch ms (signature schemes are rejected outside the tolerance window) |
| `nonce` | One-time random string (signature schemes) |
| `requestId` | Optional client correlation id — signed, echoed, trace-propagated |
| `params` | Method parameters |
| `version` | Optional contract version |

## Signing

Canonical payload (newline-separated): `method`, `timestamp`, `nonce`, `requestId`, `canonicalJson(params)`.

- `hmac`: `Base64(HMAC-SHA256(payload, secretKey))`
- `md5`: `HexLower(MD5(payload + secretKey))` — legacy scheme for clients that cannot use HMAC

Canonical JSON: recursive ordinal key sort, compact output, `null`/`undefined` excluded (0/false/"" still signed).

`EdgeSigner` exposes `SignHmac`/`VerifyHmac`/`SignMd5`/`VerifyMd5`/`BuildSignPayload`/`CanonicalizeJson` and the unified `Verify(request, secretKey)` dispatcher.
