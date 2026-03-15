---
title: "Linux"
---

# Linux

## Backend

- Package: `NativeWebView.Platform.Linux`
- Platform enum: `NativeWebViewPlatform.Linux`
- Native engine: WebKitGTK

## Supported Areas

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

## Registration

```csharp
factory.UseNativeWebViewLinux();
```

## Diagnostics Notes

`NATIVEWEBVIEW_WEBKITGTK_VERSION` can be used to assert the expected installed WebKitGTK major line during diagnostics.

## Proxy Notes

- WebKitGTK exposes website data manager proxy settings.
- `NativeWebViewLinuxProxySettingsBuilder` converts shared proxy options into a WebKitGTK-style default proxy URI plus ignore-host list.
- The current repo Linux backend remains a contract stub, so per-instance proxy settings are not yet applied at runtime.
