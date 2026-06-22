---
title: "Linux"
---

# Linux

## Backend

- Package: `NativeWebView.Platform.Linux`
- Platform enum: `NativeWebViewPlatform.Linux`
- Native engine: WebKitGTK

## Current Repo Implementation Status

- `NativeWebView`: implemented for the embedded control path on Linux/X11. The backend now creates a real GTK3/WebKitGTK child host and exposes live X11 / WebKitGTK handles.
- `NativeWebDialog`: implemented for the Linux/X11 runtime path through a top-level GTK window and embedded WebKitGTK host.
- `WebAuthenticationBroker`: implemented through the Linux dialog runtime and a WebKitGTK-backed auth window.
- Check `NativeWebViewPlatformImplementationStatusMatrix.Get(NativeWebViewPlatform.Linux)` in code when you need the honest current repo status.

## Platform Engine Capability

- Embedded view
- GPU surface rendering
- Offscreen rendering
- Dialog
- Authentication broker
- Context menu and zoom
- Printing
- New window and resource request interception
- Environment and controller options
- Native handles
- Cookie manager and command manager
- Downloads

## Registration

```csharp
factory.UseNativeWebViewLinux();
```

## Diagnostics Notes

`NATIVEWEBVIEW_WEBKITGTK_VERSION` can be used to assert the expected installed WebKitGTK major line during diagnostics.
The embedded control runtime expects GTK3/WebKitGTK on an X11-capable Linux session.

## Proxy Notes

- WebKitGTK exposes website data manager proxy settings.
- `NativeWebViewLinuxProxySettingsBuilder` converts shared proxy options into a WebKitGTK-style default proxy URI plus ignore-host list.
- The Linux `NativeWebView` and `NativeWebDialog` runtime paths now apply explicit per-instance proxy settings on the X11 runtime path.
- `Proxy.AutoConfigUrl` and embedded proxy credentials remain unsupported on the Linux runtime path.

## Current Linux Runtime Notes

- The embedded control runtime is implemented on Linux/X11 only; the backend does not currently provide a native Wayland host path.
- The dialog and broker runtime paths are also implemented on Linux/X11 only.
- `CookieDataFolder` maps to persistent WebKitGTK cookie storage when private mode is disabled.
- `UserDataFolder`, `CacheFolder`, and `SessionDataFolder` remain backend-specific configuration contracts on Linux in the current repo.
- `PrintAsync()` delegates to the native WebKitGTK print pipeline, but direct PDF export through `OutputPath` is not implemented.
- `WebAuthenticationBroker.UseHttpPost` is not currently implemented on the Linux runtime path.

## Download Notes

- Linux advertises `NativeWebViewFeature.Downloads` for `NativeWebView` and `NativeWebDialog` on the WebKitGTK runtime path.
- Downloads use WebKitGTK `download-started`, `decide-destination`, `received-data`, `failed`, and `finished` signals.
- Destination selection is handled through `DownloadStarting`; set `DestinationPath` or cancel before WebKitGTK receives the final destination.
- Progress, completion, cancellation, and failure are surfaced through download item snapshots.
- Native pause/resume and restart are not advertised in this iteration.
