---
title: "iOS"
---

# iOS

## Backend

- Package: `NativeWebView.Platform.iOS`
- Platform enum: `NativeWebViewPlatform.IOS`
- Native engine: `WKWebView`

## Supported Areas

- Embedded view
- GPU surface rendering
- Offscreen rendering
- Authentication broker
- Context menu and zoom
- New window interception
- Environment and controller options
- Native handles
- Cookie manager and command manager

## Unsupported in the Current Implementation

- Dialog backend
- Desktop-only print UI and DevTools behaviors

## Registration

```csharp
factory.UseNativeWebViewIOS();
```
