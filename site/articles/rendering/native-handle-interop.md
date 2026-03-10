---
title: "Native Handle Interop"
---

# Native Handle Interop

`NativeWebView.Interop` exposes strongly typed native handle access points so host applications can integrate the platform browser surface with other native APIs.

## Interfaces

- `INativeWebViewPlatformHandleProvider`
- `INativeWebDialogPlatformHandleProvider`

## Control Handle Access

- `NativeWebView.TryGetPlatformHandle`
- `NativeWebView.TryGetViewHandle`
- `NativeWebView.TryGetControllerHandle`

## Dialog Handle Access

- `NativeWebDialog.TryGetPlatformHandle`
- `NativeWebDialog.TryGetDialogHandle`
- `NativeWebDialog.TryGetHostWindowHandle`

## Usage Pattern

```csharp
if (WebViewControl.TryGetViewHandle(out var handle))
{
    Console.WriteLine($"Native handle: {handle.Handle} ({handle.HandleDescriptor})");
}
```

## Contract

- Return `false` when the handle is unavailable.
- Return non-zero handle values for supported providers.
- Keep descriptor strings stable for diagnostics and automation.

## When to Use It

Use native handles when you need:

- host-window integration with other native UI or rendering APIs,
- diagnostics about actual platform objects,
- advanced platform-specific customization beyond the managed facade.
