---
title: "Environment and Controller Options"
---

# Environment and Controller Options

`NativeWebView` exposes two option-request events during initialization so applications can adjust backend defaults before the platform engine is created.

## Environment Options Event

Event: `CoreWebView2EnvironmentRequested`

Option model:

- `BrowserExecutableFolder`
- `UserDataFolder`
- `Language`
- `AdditionalBrowserArguments`
- `TargetCompatibleBrowserVersion`
- `AllowSingleSignOnUsingOSPrimaryAccount`

Use this event to mutate environment defaults before initialization completes.

## Controller Options Event

Event: `CoreWebView2ControllerOptionsRequested`

Option model:

- `ProfileName`
- `IsInPrivateModeEnabled`
- `ScriptLocale`

Use this event to configure profile, private-mode, and script-locale behavior where the backend supports it.

## Example

```csharp
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

- Events are raised once per backend initialization in the current implementation.
- Platforms that cannot apply some values may still expose the event for compatibility.
- Keep event handlers deterministic because these values become part of environment reproducibility and CI diagnostics.
