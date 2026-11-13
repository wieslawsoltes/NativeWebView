# NativeWebView

Native webview stack for Avalonia that stays on top of platform-native engines instead of bundling Chromium.

[![.NET SDK 11.0.100](https://img.shields.io/badge/.NET%20SDK-11.0.100-512BD4)](https://dotnet.microsoft.com)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.5-1f6feb)](https://avaloniaui.net)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## NuGet Packages

End-user installs are typically `NativeWebView` plus the platform package for the target runtime. `NativeWebView.Dialog` and `NativeWebView.Auth` are optional facades, while `Core` and `Interop` are primarily composition units and transitive dependencies.

### Package Layout

| Package | NuGet | Purpose |
| --- | --- | --- |
| `NativeWebView` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.svg)](https://www.nuget.org/packages/NativeWebView) | Avalonia control facade API. |
| `NativeWebView.Core` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Core.svg)](https://www.nuget.org/packages/NativeWebView.Core) | Shared contracts, controllers, feature model, and backend factory. |
| `NativeWebView.Dialog` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Dialog.svg)](https://www.nuget.org/packages/NativeWebView.Dialog) | Dialog facade API. |
| `NativeWebView.Auth` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Auth.svg)](https://www.nuget.org/packages/NativeWebView.Auth) | Web authentication broker facade API. |
| `NativeWebView.Interop` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Interop.svg)](https://www.nuget.org/packages/NativeWebView.Interop) | Native handle contracts and structs. |
| `NativeWebView.Platform.Windows` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Platform.Windows.svg)](https://www.nuget.org/packages/NativeWebView.Platform.Windows) | Windows backend registration and implementation. |
| `NativeWebView.Platform.macOS` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Platform.macOS.svg)](https://www.nuget.org/packages/NativeWebView.Platform.macOS) | macOS backend registration and implementation. |
| `NativeWebView.Platform.Linux` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Platform.Linux.svg)](https://www.nuget.org/packages/NativeWebView.Platform.Linux) | Linux backend registration and implementation. |
| `NativeWebView.Platform.iOS` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Platform.iOS.svg)](https://www.nuget.org/packages/NativeWebView.Platform.iOS) | iOS backend registration and implementation. |
| `NativeWebView.Platform.Android` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Platform.Android.svg)](https://www.nuget.org/packages/NativeWebView.Platform.Android) | Android backend registration and implementation. |
| `NativeWebView.Platform.Browser` | [![NuGet](https://img.shields.io/nuget/v/NativeWebView.Platform.Browser.svg)](https://www.nuget.org/packages/NativeWebView.Platform.Browser) | Browser backend registration and implementation. |

## Features

- `NativeWebView` control for embedded native browser surfaces inside Avalonia.
- `NativeWebDialog` facade for dialog and popup browser workflows.
- `WebAuthenticationBroker` facade for OAuth and interactive sign-in.
- Platform backends for Windows, macOS, Linux, iOS, Android, and Browser.
- Optional airspace-mitigation modes: `Embedded`, `GpuSurface`, and `Offscreen`.
- One-shot PNG snapshots of the visible embedded viewport on Windows, Linux, and macOS.
- Native hover/status text on Windows and Linux, independent of the engine's native status-bar visibility.
- Diagnostics, capability reporting, render-frame export, integrity metadata, and smoke-testable sample apps.

## Current Repo Runtime Status

| Platform | `NativeWebView` control | `NativeWebDialog` | `WebAuthenticationBroker` | Per-instance proxy |
| --- | --- | --- | --- | --- |
| Windows | Implemented | Implemented | Implemented | Implemented |
| macOS | Implemented | Implemented | Implemented | Implemented on macOS 14+ |
| Linux | Implemented | Implemented | Implemented | Implemented |
| iOS | Implemented when built with the .NET 8 Apple workload | Unsupported | Implemented when built with the .NET 8 Apple workload | Implemented on iOS 17+ when built with the .NET 8 Apple workload |
| Android | Implemented when built with the .NET 8 Android workload | Unsupported | Implemented when built with the .NET 8 Android workload | Contract-only, app-wide platform API only |
| Browser | Implemented when built for the browser target | Unsupported | Implemented when built for the browser target | Unsupported |

`Features` and registered backend modules continue to describe platform capability contracts. Use `NativeWebViewPlatformImplementationStatusMatrix.Get(platform)` for the honest current repo runtime status, and `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` for proxy-specific status.

## Installation

For embedded authentication integrations, see the [navigation and messaging contract](site/articles/controls/nativewebview.md), [browser policy options](site/articles/rendering/environment-and-controller-options.md), and [macOS notes](site/articles/platforms/macos.md). Version 12.0.4.9 adds explicit navigation-cancellation reporting and completes embedded macOS messaging and state preservation. Applications must still verify native sign-in flows with the packaged runtime.

Install the Avalonia control package and the platform backend that matches the runtime you ship:

```bash
dotnet add package NativeWebView
dotnet add package NativeWebView.Platform.Windows
```

Optional packages:

- `dotnet add package NativeWebView.Dialog` for desktop dialog/window flows on Windows, macOS, and Linux
- `dotnet add package NativeWebView.Auth` for authentication callback flows on every currently supported runtime

Swap `NativeWebView.Platform.Windows` for `NativeWebView.Platform.macOS`, `NativeWebView.Platform.Linux`, `NativeWebView.Platform.iOS`, `NativeWebView.Platform.Android`, or `NativeWebView.Platform.Browser` as needed.

## Quick Start

```csharp
using NativeWebView.Controls;
using NativeWebView.Core;

NativeWebViewRuntime.EnsureCurrentPlatformRegistered();

var implementationStatus = NativeWebViewPlatformImplementationStatusMatrix.Get(
    NativeWebViewRuntime.CurrentPlatform);

if (implementationStatus.EmbeddedControl != NativeWebViewRepositoryImplementationStatus.RuntimeImplemented)
{
    throw new PlatformNotSupportedException(
        $"The embedded NativeWebView control is not implemented on {NativeWebViewRuntime.CurrentPlatform} in the current repo yet.");
}

if (!NativeWebViewRuntime.Factory.TryCreateNativeWebViewBackend(
        NativeWebViewRuntime.CurrentPlatform,
        out var backend))
{
    throw new InvalidOperationException(
        $"The platform package for {NativeWebViewRuntime.CurrentPlatform} is not registered. Reference the matching NativeWebView.Platform.* package.");
}

using var webView = new NativeWebView(backend);
webView.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "./artifacts/example-userdata";
webView.InstanceConfiguration.EnvironmentOptions.CacheFolder = "./artifacts/example-cache";
webView.InstanceConfiguration.ControllerOptions.ProfileName = "example-profile";
webView.InstanceConfiguration.DocumentStartScripts.Add(
    new NativeWebViewDocumentStartScript(
        "globalThis.applicationBridgeReady = true;",
        NativeWebViewScriptFrameScope.MainFrame));
await webView.InitializeAsync();
webView.RenderMode = NativeWebViewRenderMode.GpuSurface;
webView.RenderFramesPerSecond = 30;
webView.Navigate("https://example.com");
```

Each `NativeWebView` control instance keeps its own `InstanceConfiguration`, so multiple hosted views can carry different environment/controller defaults in the same process.
Document-start scripts are applied immediately when the collection changes and run in collection order. Configure them before the native host is created, initialization starts, or the first navigation begins; later assignment or collection mutation throws `InvalidOperationException`.

Use `NativeWebDialog` when the browser should live in a separate desktop window, and `WebAuthenticationBroker` when the main goal is an auth callback flow rather than general browsing. `NativeWebDialog` is intentionally desktop-only in the current repo runtime; `WebAuthenticationBroker` is implemented on Windows, macOS, Linux, iOS, Android, and Browser with the platform-specific limitations documented below.

Current embedded runtime implementation exists on Windows, macOS, and Linux in the default desktop build. iOS and Android runtime support is compiled from their platform-targeted backend assemblies rather than the default `net10.0` contract build, and the Browser runtime is compiled from the browser-targeted backend assembly. Windows and Linux now also ship real `NativeWebDialog` runtimes backed by native desktop windows plus their existing WebView2/WebKitGTK engines, and `WebAuthenticationBroker` runs real modal or popup browser sessions whenever the corresponding platform runtime assembly is present. The Browser implementation hosts a real embedded `iframe` through Avalonia Browser native hosting and a DOM bridge; navigation is real, but script execution, `window.chrome.webview` emulation, and new-window interception depend on same-origin access or explicit `postMessage` cooperation from the hosted page, and normal browser frame restrictions such as `X-Frame-Options` / `Content-Security-Policy: frame-ancestors` still apply. Browser authentication uses `window.open`, so popup support plus an inspectable `http` or `https` callback URL are required there; custom-scheme callbacks and `UseHttpPost` are not supported on the current browser runtime path. The iOS implementation hosts `WKWebView` through a backend-owned `UIView` attachment path and a modal authentication controller when `NativeWebView.Platform.iOS` is built with the .NET 10 Apple workload; `NativeWebDialog` remains unsupported there. The Android implementation hosts `android.webkit.WebView` through a backend-owned child `View` attachment and a dedicated authentication activity when `NativeWebView.Platform.Android` is built with the .NET 10 Android workload; `NativeWebDialog` remains unsupported there. Per-instance proxy application is effective on the Windows and Linux `NativeWebView` and `NativeWebDialog` runtime paths, on macOS 14+ for `NativeWebView` and `NativeWebDialog`, and on iOS 17+ for the embedded `NativeWebView` control when the iOS runtime assembly is present. The macOS and iOS implementations support explicit `http`, `https`, and `socks5` proxy servers, credentials, and bypass domains, and use a dedicated `WKWebsiteDataStore` identity derived from the instance configuration so proxied views do not fall back to private-browsing storage semantics. On Linux, explicit `http`, `https`, and `socks` proxies are runtime-applied on the X11 control/dialog paths; `AutoConfigUrl`, embedded proxy credentials, and direct PDF export are not currently applied there. On Android, the official AndroidX proxy override remains app-wide, so per-instance proxy configuration is still contract-only on the current runtime path. Browser targets remain unsupported for per-instance proxy application because the host browser does not expose per-instance engine proxy control. `WebAuthenticationBroker.UseHttpPost` currently remains unsupported on all runtime backends in this repo.

Direct connections are available through `InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions { NoProxy = true }` before initialization. Windows and Linux use native direct modes; macOS uses authenticated loopback SOCKS5 forwarding and a separate persistent Direct profile. `SupportsNoProxy` on the proxy support matrix describes implementation capability, subject to OS/runtime requirements. See [NoProxy configuration](site/articles/rendering/no-proxy.md) and [native qualification gates](tests/proxy-qualification.md); macOS 14 execution and broader proxy qualification remain release requirements.

Use `NativeWebViewPlatformImplementationStatusMatrix.Get(platform)` to inspect the honest current repo runtime status for each target. Use `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` for proxy-specific status. The current core package also exposes `NativeWebViewWindowsProxyArgumentsBuilder` and `NativeWebViewLinuxProxySettingsBuilder` so future or custom backends can translate shared proxy options into WebView2/WebKitGTK-specific configuration payloads without duplicating parsing logic.
Exact `UserDataFolder`/`CacheFolder`/`CookieDataFolder`/`SessionDataFolder` behavior remains backend-specific. On Linux, `UserDataFolder` (or `SessionDataFolder` as a fallback) and `CacheFolder` configure the WebKitGTK website data manager, while `CookieDataFolder` configures its persistent cookie database; private mode uses an ephemeral web context and ignores those persistent locations. The macOS and iOS `WKWebView` runtime paths use storage-path values and `ProfileName` as part of an isolated data-store identity rather than direct physical directory mapping. Private mode uses a non-persistent data store; dedicated persistent identities require macOS 14+ or iOS 17+.

## Rendering Modes

- `Embedded` keeps the native child view hosted directly for maximum fidelity.
- `GpuSurface` captures frames into a reusable Avalonia-backed surface.
- `Offscreen` captures frames offscreen for fully managed composition paths.

Useful runtime APIs:

- `webView.SupportsRenderMode(mode)`
- `webView.IsUsingSyntheticFrameSource`
- `webView.RenderDiagnosticsMessage`
- `webView.RenderStatistics` and `webView.GetRenderStatisticsSnapshot()`
- `webView.ResetRenderStatistics()`
- `await webView.CaptureRenderFrameAsync()`
- `await webView.CaptureSnapshotAsync()` for a one-shot embedded viewport PNG
- `await webView.SaveRenderFrameAsync("artifacts/frame.png")`
- `await webView.SaveRenderFrameWithMetadataAsync("artifacts/frame.png", "artifacts/frame.json")`

Render sidecar metadata includes `FrameId`, `CapturedAtUtc`, `RenderMode`, `Origin`, `PixelDataLength`, and `PixelDataSha256`. Integrity verification requires matching `FormatVersion`.

`StatusText` and `StatusTextChanged` expose bounded native hover/link status on platforms that advertise `NativeWebViewFeature.StatusText`. Setting `IsStatusBarEnabled` to `false` hides the engine-provided status UI but does not suppress these events. Embedded snapshot capture is best-effort: cancellation propagates, while unsupported, uninitialized, unavailable, and failed captures return `null`.

`ZoomFactorChanged` reports effective programmatic zoom changes on every backend. Windows, Linux, and macOS additionally advertise `NativeWebViewFeature.ZoomFactorChangeNotification` and report native or user-driven zoom changes.

## Samples

Run the desktop feature explorer:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug --framework net11.0
```

Run the deterministic smoke matrix used by CI:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug --framework net11.0 -- --smoke
```

## Diagnostics and Release Validation

Runtime readiness check:

```csharp
NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
var diagnostics = NativeWebViewRuntime.GetCurrentPlatformDiagnostics();

if (!diagnostics.IsReady)
{
    throw new InvalidOperationException(
        $"Platform prerequisites are not satisfied for {diagnostics.Platform}.");
}

NativeWebViewDiagnosticsValidator.EnsureReady(diagnostics);
```

Applications can offer a platform-neutral repair action when an issue supplies a remediation:

```csharp
var installRuntime = diagnostics.Issues
    .Select(issue => issue.Remediation)
    .FirstOrDefault(remediation => remediation?.Kind == NativeWebViewDiagnosticRemediationKind.InstallRuntime);
```

Generate release-facing diagnostics and gate artifacts:

```bash
./scripts/run-platform-diagnostics-report.sh --configuration Release --platform all --output artifacts/diagnostics/platform-diagnostics-report.json --markdown-output artifacts/diagnostics/platform-diagnostics-report.md --blocking-baseline ci/baselines/blocking-issues-baseline.txt --comparison-markdown-output artifacts/diagnostics/blocking-regression.md --comparison-json-output artifacts/diagnostics/blocking-regression.json --comparison-evaluation-markdown-output artifacts/diagnostics/gate-evaluation.md --require-baseline-sync --allow-not-ready
./scripts/validate-diagnostics-exit-code-contract.sh --configuration Release --no-build --output-dir artifacts/diagnostics/exit-code-contract --baseline ci/baselines/blocking-issues-baseline.txt --fingerprint-baseline ci/baselines/diagnostics-fingerprint-baseline.txt
./scripts/validate-nuget-packages.sh --package-dir artifacts/packages --markdown-output artifacts/packages/package-validation.md
```

`blocking-regression.json` includes deterministic evaluation fingerprints and structured `gateFailures` metadata for automation and release triage. Package validation verifies every `.nupkg` and `.snupkg`, packed README/license files, nuspec metadata, and expected package dependencies.

## Documentation

- Hosted docs: [wieslawsoltes.github.io/NativeWebView](https://wieslawsoltes.github.io/NativeWebView/)
- Getting started: [Quickstart](https://wieslawsoltes.github.io/NativeWebView/articles/getting-started/quickstart/)
- Control surface: [NativeWebView](https://wieslawsoltes.github.io/NativeWebView/articles/controls/nativewebview/)
- Desktop dialog surface: [NativeWebDialog](https://wieslawsoltes.github.io/NativeWebView/articles/controls/nativewebdialog/)
- Auth surface: [WebAuthenticationBroker](https://wieslawsoltes.github.io/NativeWebView/articles/controls/webauthenticationbroker/)
- Rendering and interop: [Render Modes](https://wieslawsoltes.github.io/NativeWebView/articles/rendering/render-modes/)
- Platform notes: [Platforms](https://wieslawsoltes.github.io/NativeWebView/articles/platforms/)
- Diagnostics and operations: [Diagnostics](https://wieslawsoltes.github.io/NativeWebView/articles/diagnostics/)
- API reference: [wieslawsoltes.github.io/NativeWebView/api](https://wieslawsoltes.github.io/NativeWebView/api)

## CI and Release

GitHub Actions workflows:

- `CI`: quality gate, matrix build/test, release pack, diagnostics/report artifacts, and NuGet package validation.
- `Release`: tag-driven `v*` pack/publish flow with release notes, diagnostics artifacts, package validation, NuGet push, and GitHub Release publishing.
- `Docs`: Lunet build and GitHub Pages deployment from `site/.lunet/build/www`.
- `Extended Validation`: scheduled/manual Playwright, iOS simulator, and Android emulator validation.

Local release dry run:

Use the .NET 11 SDK (see `global.json`). Libraries, desktop samples, and core tests target both `net10.0` and `net11.0`; .NET 11 builds opt in to runtime async. Workload-enabled backends also include .NET 11 platform targets. Install the matching mobile or WebAssembly workloads before building those targets.

Launch scripts select `net11.0` by default. Set `NATIVEWEBVIEW_FRAMEWORK=net10.0` to run the .NET 10 version.

```bash
dotnet restore NativeWebView.sln
dotnet build NativeWebView.sln -c Release
dotnet test NativeWebView.sln -c Release --no-build
dotnet pack NativeWebView.sln -c Release --no-build -o artifacts/packages
bash ./scripts/validate-nuget-packages.sh --package-dir artifacts/packages --markdown-output artifacts/packages/package-validation.md
```

Local docs run:

```bash
./build-docs.sh
./serve-docs.sh
```

## License

MIT. See [LICENSE](LICENSE).
