---
title: "Windows"
---

# Windows

## Backend

- Package: `NativeWebView.Platform.Windows`
- Platform enum: `NativeWebViewPlatform.Windows`
- Native engine: Microsoft Edge WebView2

## Supported Areas

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
