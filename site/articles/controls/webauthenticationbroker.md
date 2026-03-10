---
title: "WebAuthenticationBroker"
---

# WebAuthenticationBroker

`WebAuthenticationBroker` exposes a unified authentication flow surface across supported backends.

## API

- `AuthenticateAsync(Uri requestUri, Uri callbackUri, WebAuthenticationOptions options, CancellationToken cancellationToken)`

## Options

- `None`
- `SilentMode`
- `UseTitle`
- `UseHttpPost`
- `UseCorporateNetwork`
- `UseWebAuthenticationBroker`

## Result

| Field | Meaning |
| --- | --- |
| `ResponseStatus` | `Success`, `UserCancel`, or `ErrorHttp`. |
| `ResponseData` | Callback payload when available. |
| `ResponseErrorDetail` | Backend-specific error code when the status is `ErrorHttp`. |

## Security Validation

Controller-level guards validate that:

- request URI is absolute and uses `http` or `https`,
- callback URI is absolute,
- callback scheme is not `javascript`, `data`, `file`, `about`, or `blob`,
- request and callback URIs do not include `UserInfo`.

## Typical Usage Pattern

```csharp
using NativeWebView.Auth;
using NativeWebView.Core;

NativeWebViewRuntime.EnsureCurrentPlatformRegistered();

using var broker = new WebAuthenticationBroker();
var result = await broker.AuthenticateAsync(
    new Uri("https://example.com/auth"),
    new Uri("https://example.com/callback"),
    WebAuthenticationOptions.None,
    cancellationToken);
```

## Related

- [Platform Notes](../platforms/readme.md)
- [Platform Prerequisites](../diagnostics/platform-prerequisites.md)
