---
title: "NativeWebDialog"
---

# NativeWebDialog

`NativeWebDialog` is the desktop dialog or window facade for browser workflows that do not belong inside the main Avalonia visual tree.

## Availability

- Windows: supported.
- macOS: supported.
- Linux: supported.
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
- `TryGetPlatformHandle`, `TryGetDialogHandle`, `TryGetHostWindowHandle`

## Main Events

- Visibility: `Shown`, `Closed`
- Navigation: `NavigationStarted`, `NavigationCompleted`
- Messaging and interception: `WebMessageReceived`, `NewWindowRequested`, `WebResourceRequested`, `ContextMenuRequested`

## Typical Usage Pattern

```csharp
using NativeWebView.Dialog;
using NativeWebView.Core;

NativeWebViewRuntime.EnsureCurrentPlatformRegistered();

using var dialog = new NativeWebDialog();
dialog.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
{
    Server = "http://localhost:8888",
    BypassList = "localhost;127.0.0.1"
};
dialog.Show(new NativeWebDialogShowOptions
{
    Title = "NativeWebView Sample Dialog",
    Width = 900,
    Height = 640
});
dialog.Navigate("https://example.com/dialog");
```

## Notes

- Dialog backends remain capability-driven just like the embedded control.
- In the current repo implementation, per-instance proxy application is effective on macOS 14+ only.
- Print UI and DevTools depend on the platform backend.
- Unsupported mobile/browser targets return unsupported backend contracts instead of silently no-op behavior.
