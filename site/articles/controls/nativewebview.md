---
title: "NativeWebView"
---

# NativeWebView

`NativeWebView` is the embedded browser control for Avalonia. It derives from `NativeControlHost` and resolves the active platform backend either from an explicitly supplied backend or from `NativeWebViewRuntime`.

## Core Capabilities

- Navigation lifecycle and history state.
- Script execution and web message posting.
- Feature toggles for DevTools, context menu, status bar, and zoom controls where supported.
- Printing and print UI where supported.
- Cookie manager and command manager access where supported.
- Native handle interop.
- Airspace-mitigation render modes: `Embedded`, `GpuSurface`, and `Offscreen`.

## Key Properties

| Property | Purpose |
| --- | --- |
| `Source`, `CurrentUrl` | Current navigation target and current URL. |
| `IsInitialized` | Indicates whether backend initialization completed. |
| `CanGoBack`, `CanGoForward` | Navigation history state. |
| `IsDevToolsEnabled`, `IsContextMenuEnabled`, `IsStatusBarEnabled`, `IsZoomControlEnabled` | Capability-backed runtime toggles. |
| `ZoomFactor`, `HeaderString`, `UserAgentString` | Browser behavior configuration. |
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
- Messaging and interception: `WebMessageReceived`, `NewWindowRequested`, `WebResourceRequested`, `ContextMenuRequested`
- Window integration: `RequestCustomChrome`, `RequestParentWindowPosition`, `BeginMoveDrag`, `BeginResizeDrag`, `DestroyRequested`
- Rendering: `RenderFrameCaptured`

## Unsupported Operations

Backends advertise support through `Features`. When a backend does not support an operation, capability flags expose that in advance and the method call throws `PlatformNotSupportedException` if invoked anyway.

## Typical Initialization Pattern

```csharp
NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
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

## Related

- [Render Modes](../rendering/render-modes.md)
- [Environment and Controller Options](../rendering/environment-and-controller-options.md)
- [Native Handle Interop](../rendering/native-handle-interop.md)
