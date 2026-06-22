---
title: "NativeWebDialog"
---

# NativeWebDialog

`NativeWebDialog` is the desktop dialog or window facade for browser workflows that do not belong inside the main Avalonia visual tree.

## Availability

- Windows: implemented.
- macOS: implemented.
- Linux: implemented.
- iOS, Android, Browser: unsupported by design in the current implementation.

## Main Properties

| Property | Purpose |
| --- | --- |
| `IsVisible`, `CurrentUrl` | Dialog visibility and current URL. |
| `InstanceConfiguration` | Per-instance environment/controller defaults, including proxy settings on supported runtimes. |
| `CanGoBack`, `CanGoForward` | Navigation history state. |
| `IsDevToolsEnabled`, `IsContextMenuEnabled`, `IsStatusBarEnabled`, `IsZoomControlEnabled` | Desktop browser UI toggles where supported. |
| `ZoomFactor`, `HeaderString`, `UserAgentString` | Runtime dialog browser configuration. |
| `LifecycleState` | Backend controller state. |

## Main Methods

- `Show`, `Close`, `Move`, `Resize`
- `Navigate`, `Reload`, `Stop`, `GoBack`, `GoForward`
- `ExecuteScriptAsync`
- `PostWebMessageAsJsonAsync`, `PostWebMessageAsStringAsync`
- `OpenDevToolsWindow`
- `PrintAsync`, `ShowPrintUiAsync`
- `SetZoomFactor`, `SetUserAgent`, `SetHeader`
- `TryGetDownloadManager`
- `TryGetPlatformHandle`, `TryGetDialogHandle`, `TryGetHostWindowHandle`

## Main Events

- Visibility: `Shown`, `Closed`
- Navigation: `NavigationStarted`, `NavigationCompleted`
- Downloads: `DownloadStarting`, `DownloadStarted`, `DownloadChanged`, `DownloadCompleted`
- Messaging and interception: `WebMessageReceived`, `NewWindowRequested`, `WebResourceRequested`, `ContextMenuRequested`

## Typical Usage Pattern

```csharp
using NativeWebView.Dialog;
using NativeWebView.Core;

NativeWebViewRuntime.EnsureCurrentPlatformRegistered();

using var dialog = new NativeWebDialog();
dialog.Show(new NativeWebDialogShowOptions
{
    Title = "NativeWebView Sample Dialog",
    Width = 900,
    Height = 640
});
dialog.Navigate("https://example.com/dialog");
```

## Notes

- Dialog backends remain capability-driven just like the embedded control. Real dialog runtime paths now exist on Windows, macOS, and Linux.
- Check `NativeWebViewPlatformImplementationStatusMatrix.Get(...)` when you need to distinguish implemented dialog paths from unsupported mobile/browser targets.
- Windows and Linux dialog backends reuse the same WebView2/WebKitGTK runtime pipelines as their embedded `NativeWebView` hosts, so per-instance storage and proxy options flow through `InstanceConfiguration`.
- Windows and Linux dialog backends also expose the download manager when `NativeWebViewFeature.Downloads` is advertised. Use `DownloadStarting` with a deferral to choose a destination or cancel before transfer starts.
- In the current repo implementation, per-instance proxy application is effective on Windows, Linux, and macOS 14+ dialog runtime paths.
- Print UI and DevTools depend on the platform backend.
- Unsupported mobile/browser targets return unsupported backend contracts instead of silently no-op behavior.
