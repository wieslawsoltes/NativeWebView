---
title: "Android"
---

# Android

## Backend

- Package: `NativeWebView.Platform.Android`
- Platform enum: `NativeWebViewPlatform.Android`
- Native engine: `android.webkit.WebView`

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
factory.UseNativeWebViewAndroid();
```

## Diagnostics Notes

Diagnostics use `ANDROID_API_LEVEL` for minimum API-level enforcement (`24+`).
