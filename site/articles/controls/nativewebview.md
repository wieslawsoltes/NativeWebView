---
title: "NativeWebView"
---

# NativeWebView

`NativeWebView` is the embedded browser control for Avalonia. It derives from `NativeControlHost` and resolves the active platform backend either from an explicitly supplied backend or from `NativeWebViewRuntime`.

Current repo runtime status: the embedded native host path is implemented on Windows, macOS, and Linux in the default desktop build. Linux uses a GTK3/WebKitGTK child host on X11. iOS and Android runtime support comes from their platform-targeted backend assemblies rather than the default `net10.0` contract build, and Browser support comes from the browser-targeted backend assembly. Browser uses Avalonia Browser native hosting plus an embedded `iframe`; navigation is real, but script execution and `window.chrome.webview`-style messaging are limited by same-origin browser security rules. Use `NativeWebViewPlatformImplementationStatusMatrix.Get(...)` when you need the honest current repo status for the current app build.

Companion surfaces are available when embedding is not the right fit: [`NativeWebDialog`](nativewebdialog.md) for desktop popup windows and [`WebAuthenticationBroker`](webauthenticationbroker.md) for auth callback flows.

## Core Capabilities

- Navigation lifecycle and history state.
- Script execution and web message posting.
- Feature toggles for DevTools, context menu, status bar, and zoom controls where supported.
- Printing and print UI where supported.
- Cookie manager and command manager access where supported.
- Favicon change notifications and favicon byte retrieval where supported, including raw SVG assets through the `Original` format.
- Native handle interop.
- Airspace-mitigation render modes: `Embedded`, `GpuSurface`, and `Offscreen`.

## Key Properties

| Property | Purpose |
| --- | --- |
| `Source`, `CurrentUrl` | Current navigation target and current URL. |
| `IsInitialized` | Indicates whether backend initialization completed. |
| `InstanceConfiguration` | Per-instance environment/controller defaults, including proxy and storage directories. |
| `CanGoBack`, `CanGoForward` | Navigation history state. |
| `IsDevToolsEnabled`, `IsContextMenuEnabled`, `IsStatusBarEnabled`, `IsZoomControlEnabled` | Capability-backed runtime toggles. |
| `ZoomFactor`, `HeaderString`, `UserAgentString` | Backend behavior configuration. |
| `RenderMode`, `RenderFramesPerSecond` | Render strategy and frame pump rate for composited modes. |
| `IsUsingSyntheticFrameSource`, `RenderDiagnosticsMessage`, `RenderStatistics` | Composition diagnostics and capture statistics. |
| `MacOsCompositedPassthroughOverride` | Manual passthrough override for macOS composited mode. |

## Main Methods

- `InitializeAsync`
- `Navigate`, `Reload`, `Stop`, `GoBack`, `GoForward`
- `ExecuteScriptAsync`
- `PostWebMessageAsJsonAsync`, `PostWebMessageAsStringAsync`
- `OpenDevToolsWindow`
- `PrintAsync`, `ShowPrintUiAsync`
- `GetFaviconAsync`
- `SetZoomFactor`, `SetUserAgent`, `SetHeader`
- `TryGetCommandManager`, `TryGetCookieManager`
- `MoveFocus`
- `SupportsRenderMode`
- `SetCompositedPassthroughOverride`
- `CaptureRenderFrameAsync`, `SaveRenderFrameAsync`, `SaveRenderFrameWithMetadataAsync`
- `GetRenderStatisticsSnapshot`, `ResetRenderStatistics`
- `TryGetPlatformHandle`, `TryGetViewHandle`, `TryGetControllerHandle`

## Main Events

- Initialization: `CoreWebView2Initialized`, `CoreWebView2EnvironmentRequested`, `CoreWebView2ControllerOptionsRequested`
- Navigation: `NavigationStarted`, `NavigationCompleted`, `NavigationHistoryChanged`
- Favicons: `FaviconChanged`
- Messaging and interception: `WebMessageReceived`, `NewWindowRequested`, `WebResourceRequested`, `ContextMenuRequested`
- Window integration: `RequestCustomChrome`, `RequestParentWindowPosition`, `BeginMoveDrag`, `BeginResizeDrag`, `DestroyRequested`
- Rendering: `RenderFrameCaptured`

## Unsupported Operations

Backends advertise support through `Features`. When a backend does not support an operation, capability flags expose that in advance and the method call throws `PlatformNotSupportedException` if invoked anyway.

`Features` describe platform capability contracts. They are not a substitute for `NativeWebViewPlatformImplementationStatusMatrix` when you need to know whether this repository already ships a real native host path for the current target.

## Typical Initialization Pattern

```csharp
NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
WebViewControl.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "./artifacts/webview-a";
WebViewControl.InstanceConfiguration.ControllerOptions.ProfileName = "webview-a";
await WebViewControl.InitializeAsync();
WebViewControl.Navigate("https://example.com");
```

## Render Diagnostics

Use composited-mode diagnostics when you need capture telemetry or airspace investigation:

- `IsUsingSyntheticFrameSource`
- `RenderDiagnosticsMessage`
- `RenderStatistics`
- `GetRenderStatisticsSnapshot`
- `CaptureRenderFrameAsync`
- `SaveRenderFrameWithMetadataAsync`
- `ResetRenderStatistics`

Render statistics expose capture counters and last-frame metadata, including attempt, success, failure, and skip counts together with last failure details.

Sidecar metadata includes `FrameId`, `CapturedAtUtc`, `RenderMode`, `Origin`, `PixelDataLength`, and `PixelDataSha256`.
Use `NativeWebViewRenderFrameMetadataSerializer.ReadFromFileAsync` to load the sidecar JSON and `TryVerifyIntegrity` to validate captured frames against the stored integrity fields.

## Favicon Notes

- Check `Features.Supports(NativeWebViewFeature.Favicon)` before calling `GetFaviconAsync`.
- `GetFaviconAsync(NativeWebViewFaviconFormat.Original)` returns raw favicon bytes when available. SVG favicons are returned as `image/svg+xml` bytes and are not rasterized by the control.
- Windows uses WebView2 favicon APIs for PNG/JPEG and downloads the favicon URI for SVG originals. Linux and iOS resolve declared document favicon links and download the resolved URI. Android uses native bitmap callbacks for PNG/JPEG and document-link resolution for SVG originals.
- Browser targets do not currently advertise favicon support because cross-origin iframe restrictions make document favicon discovery unreliable.

## Proxy Notes

- Check `NativeWebViewPlatformImplementationStatusMatrix.Get(platform)` before assuming the current target has a real embedded runtime path.
- Check `Features.Supports(NativeWebViewFeature.ProxyConfiguration)` before relying on runtime proxy application.
- In the current repo implementation, per-instance proxy application is effective on the embedded Windows and Linux `NativeWebView` controls, on macOS 14+, and on iOS 17+ when the iOS backend is built with the .NET 8 Apple workload.
- The macOS runtime path supports explicit `http`, `https`, and `socks5` proxy servers plus bypass domains.
- The iOS runtime path supports explicit `http`, `https`, and `socks5` proxy servers, credentials, and bypass domains on iOS 17+.
- The Windows runtime path applies proxy settings through WebView2 `AdditionalBrowserArguments`.
- The Linux runtime path applies explicit proxy settings through WebKitGTK website data manager settings on X11.
- The Android runtime path is real, but Android proxy override remains app-wide and is not applied per `WebView` instance by this repo.
- The Browser runtime path is real, but browser security rules still apply: pages may refuse framing, and cross-origin pages cannot be script-driven through the embedded iframe host.
- `Proxy.AutoConfigUrl` is not applied by the current Apple runtime integrations in this repo.

## Related

- [Render Modes](../rendering/render-modes.md)
- [Environment and Controller Options](../rendering/environment-and-controller-options.md)
- [Native Handle Interop](../rendering/native-handle-interop.md)
