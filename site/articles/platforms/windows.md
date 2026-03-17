---
title: "Windows"
---

# Windows

## Backend

- Package: `NativeWebView.Platform.Windows`
- Platform enum: `NativeWebViewPlatform.Windows`
- Native engine: Microsoft Edge WebView2

## Current Repo Implementation Status

- `NativeWebView`: implemented. The control now creates a real child HWND and WebView2 controller when hosted through the Avalonia `NativeWebView` control.
- `NativeWebDialog`: implemented. The backend now creates a real top-level HWND and hosts the WebView2 runtime inside it.
- `WebAuthenticationBroker`: implemented through the Windows dialog runtime and a WebView2-backed auth window.
- Check `NativeWebViewPlatformImplementationStatusMatrix.Get(NativeWebViewPlatform.Windows)` in code when you need the honest current repo status.

## Platform Engine Capability

- Embedded view
- GPU surface rendering
- Offscreen rendering
- Dialog
- Authentication broker
- DevTools, context menu, status bar, zoom
- Printing and print UI
- New window and resource request interception
- Environment and controller options
- Native handles
- Cookie manager and command manager

## Registration

```csharp
factory.UseNativeWebViewWindows();
```

## Diagnostics Notes

Use `NATIVEWEBVIEW_WEBVIEW2_RUNTIME_PATH` when you need an explicit runtime path override. If it is set, the path must exist.

Windows apps that host `NativeWebView` through Avalonia `NativeControlHost` need an application manifest with supported OS declarations. The desktop sample now includes `app.manifest`; custom Windows hosts should ship the same kind of manifest to avoid the Avalonia child-window creation failure.

When WebView2 rejects controller options with an invalid-argument failure, the Windows backend now retries controller creation without those options so initialization can still succeed on runtimes that do not accept the requested profile or locale settings.

## Proxy Notes

- WebView2 can be configured via environment options and Chromium proxy arguments.
- `NativeWebViewWindowsProxyArgumentsBuilder` converts shared proxy options into `AdditionalBrowserArguments` payloads for WebView2-style integrations.
- The Windows `NativeWebView` and `NativeWebDialog` runtime paths apply per-instance proxy settings by merging them into WebView2 `AdditionalBrowserArguments`.

## Authentication Notes

- The current Windows `WebAuthenticationBroker` implementation uses a dedicated dialog-hosted WebView2 session and completes when navigation reaches the callback scheme/host/path.
- `UseHttpPost` is not currently implemented on the Windows runtime path.
