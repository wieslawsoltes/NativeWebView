---
title: "Environment and Controller Options"
---

# Environment and Controller Options

`NativeWebView` exposes two option-request events during initialization so applications can adjust backend defaults before the platform engine is created. For multi-instance hosting, use `NativeWebView.InstanceConfiguration` to seed per-instance defaults before these public handlers run.

## Environment Options Event

Event: `CoreWebView2EnvironmentRequested`

Option model:

- `BrowserExecutableFolder`
- `UserDataFolder`
- `CacheFolder`
- `CookieDataFolder`
- `SessionDataFolder`
- `Language`
- `AdditionalBrowserArguments`
- `TargetCompatibleBrowserVersion`
- `AllowSingleSignOnUsingOSPrimaryAccount`
- `Proxy.Server`
- `Proxy.BypassList`
- `Proxy.AutoConfigUrl`

Use this event to mutate environment defaults before initialization completes.

## Controller Options Event

Event: `CoreWebView2ControllerOptionsRequested`

Option model:

- `ProfileName`
- `IsInPrivateModeEnabled`
- `ScriptLocale`

Use this event to configure profile, private-mode, and script-locale behavior where the backend supports it.

## Per-Instance Proxy Support Matrix

| Platform | Native engine capability | Current repo status |
| --- | --- | --- |
| macOS | `WKWebsiteDataStore.proxyConfigurations` (`macOS 14+`) | Implemented for `NativeWebView` and `NativeWebDialog` with explicit `http`, `https`, and `socks5` proxy servers plus bypass domains. |
| Windows | WebView2 environment arguments / Chromium proxy flags | Not yet runtime-applied because the current Windows backend remains a contract stub. |
| Linux | WebKitGTK website data manager proxy settings | Not yet runtime-applied because the current Linux backend remains a contract stub. |
| iOS | `WKWebsiteDataStore.proxyConfigurations` (`iOS 17+`) | Not yet runtime-applied because the current iOS backend remains a contract stub. |
| Android | AndroidX `ProxyController` | Unsupported for per-instance use because the official override is app-wide, not per-WebView. |
| Browser | Host browser integration | Unsupported because the hosted browser target does not expose per-instance engine proxy control in the current implementation. |

Use `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` when you need the same support verdict in code rather than documentation.

## Translation Helpers

The core package now exposes backend-translation helpers for platforms that have official runtime APIs but do not yet have real runtime backends in this repo:

- `NativeWebViewWindowsProxyArgumentsBuilder.Build(...)` / `Merge(...)`
  Converts `NativeWebViewProxyOptions` into Chromium/WebView2 command-line arguments for `AdditionalBrowserArguments`.
- `NativeWebViewLinuxProxySettingsBuilder.Build(...)`
  Converts `NativeWebViewProxyOptions` into a WebKitGTK-style default-proxy URI plus ignore-host list.

These helpers are groundwork for future backends or custom integrations; they do not by themselves make the current Windows or Linux repo backends runtime-capable.

## Example

```csharp
WebViewControl.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "./artifacts/sample-a/userdata";
WebViewControl.InstanceConfiguration.EnvironmentOptions.CacheFolder = "./artifacts/sample-a/cache";
WebViewControl.InstanceConfiguration.EnvironmentOptions.CookieDataFolder = "./artifacts/sample-a/cookies";
WebViewControl.InstanceConfiguration.EnvironmentOptions.SessionDataFolder = "./artifacts/sample-a/session";
WebViewControl.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
{
    Server = "http://localhost:8888",
    BypassList = "localhost;127.0.0.1"
};
WebViewControl.InstanceConfiguration.ControllerOptions.ProfileName = "sample-a";

WebViewControl.CoreWebView2EnvironmentRequested += (_, e) =>
{
    e.Options.Language ??= "en-US";
    e.Options.UserDataFolder ??= "./artifacts/sample-webview-userdata";
};

WebViewControl.CoreWebView2ControllerOptionsRequested += (_, e) =>
{
    e.Options.ProfileName ??= "sample-profile";
    e.Options.ScriptLocale ??= "en-US";
};
```

## Notes

- `InstanceConfiguration` is copied per control instance, so multiple `NativeWebView` controls can start from the same template and then diverge safely.
- Exact `UserDataFolder` / `CacheFolder` / `CookieDataFolder` / `SessionDataFolder` semantics are backend-specific. In the current repo, unsupported runtime backends still surface these values as contracts, and the macOS proxy runtime path uses them as part of isolated data-store identity rather than direct physical directory mapping.
- On macOS, assign proxy settings before the control is attached or before a dialog is shown so the native `WKWebView` can be created with a dedicated data store.
- `Proxy.AutoConfigUrl` remains contract-only in the current repo implementation; the macOS runtime path applies explicit server proxies only.
- Events are raised once per backend initialization in the current implementation.
- Platforms that cannot apply some values may still expose the event for compatibility.
- Keep event handlers deterministic because these values become part of environment reproducibility and CI diagnostics.
