---
title: "Browser"
---

# Browser

## Backend

- Package: `NativeWebView.Platform.Browser`
- Platform enum: `NativeWebViewPlatform.Browser`
- Native engine: browser-host integration for WebAssembly/browser targets

## Supported Areas

- Embedded view
- GPU surface rendering
- Offscreen rendering
- Authentication broker
- New window interception
- Environment and controller options
- Native handles
- Cookie manager and command manager

## Unsupported in the Current Implementation

- Dialog backend
- Desktop windowing features

## Registration

```csharp
factory.UseNativeWebViewBrowser();
```

## Diagnostics Notes

Set `NATIVEWEBVIEW_BROWSER_POPUP_SUPPORT=false` or `0` to force the popup-support warning path during diagnostics testing.

## Proxy Notes

- Browser targets run inside the host browser’s networking stack.
- The current implementation does not expose per-instance proxy control for browser-hosted `NativeWebView` instances.
