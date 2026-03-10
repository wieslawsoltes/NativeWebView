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
