---
title: "macOS"
---

# macOS

## Backend

- Package: `NativeWebView.Platform.macOS`
- Platform enum: `NativeWebViewPlatform.MacOS`
- Native engine: `WKWebView`

## Supported Areas

- Embedded view
- GPU surface rendering
- Offscreen rendering
- Dialog
- Authentication broker
- Context menu and zoom
- Printing and print UI
- New window and resource request interception
- Environment and controller options
- Native handles
- Cookie manager and command manager

## Registration

```csharp
factory.UseNativeWebViewMacOS();
```

## Composition Notes

macOS supports composited hosting paths and passthrough decisions for scenarios such as hardware-accelerated video playback. Review [Render Modes](../rendering/render-modes.md) before forcing capture-based composition across all content types.

## Proxy Notes

- Per-instance proxy application is implemented for `NativeWebView` and `NativeWebDialog` on `macOS 14+`.
- The current runtime path uses a dedicated persistent `WKWebsiteDataStore` identity derived from the instance configuration.
- Explicit `http`, `https`, and `socks5` proxy servers plus bypass domains are supported.
- PAC (`AutoConfigUrl`) is not applied by the current macOS integration.
