---
title: "Platform Support Matrix"
---

# Platform Support Matrix

## Current Repo Runtime Status

| Platform | `NativeWebView` control | `NativeWebDialog` | `WebAuthenticationBroker` | Per-instance proxy | Favicon |
| --- | --- | --- | --- | --- | --- |
| Windows | Implemented | Implemented | Implemented | Implemented | Implemented, including SVG originals via favicon URI |
| macOS | Implemented | Implemented | Implemented | Implemented on macOS 14+ | Not advertised by the current embedded backend |
| Linux | Implemented | Implemented | Implemented | Implemented | Implemented through document favicon link resolution |
| iOS | Implemented when built with the .NET 8 Apple workload | Unsupported | Implemented when built with the .NET 8 Apple workload | Implemented on iOS 17+ when built with the .NET 8 Apple workload | Implemented in the iOS runtime assembly |
| Android | Implemented when built with the .NET 8 Android workload | Unsupported | Implemented when built with the .NET 8 Android workload | Contract-only, app-wide platform API only | Implemented in the Android runtime assembly |
| Browser | Implemented when built for the browser target | Unsupported | Implemented when built for the browser target | Unsupported | Unsupported |

Use `NativeWebViewPlatformImplementationStatusMatrix.Get(platform)` to inspect the current repo status in code. Use `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` for proxy-specific status.

## Capability Contract Notes

- Registered backend modules and `Features` continue to describe the broader platform capability contract for that engine family.
- Current repo runtime status is intentionally tracked separately so docs and applications can distinguish stubbed contracts from implemented native host paths.
- Today, Windows, macOS, and Linux have real embedded `NativeWebView` control hosts in the default desktop build. iOS and Android runtime paths are built from their platform-targeted backend assemblies rather than the default `net10.0` contract build, and the Browser runtime is built from the browser-targeted backend assembly and hosts an `iframe` plus popup/browser-auth integration through Avalonia Browser native control hosting.

## Practical Notes

- `NativeWebDialog` is a desktop surface in this repo. For mobile and browser auth flows, use `WebAuthenticationBroker` instead of expecting dialog parity.
- Browser auth is popup-based and currently requires popup support plus an inspectable `http` or `https` callback URL.
- Per-instance proxy status should be read separately from general runtime status. Use `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` when proxy behavior matters to your deployment.
