# NativeWebView

Native webview stack for Avalonia that stays on top of platform-native engines instead of bundling Chromium.

[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)](https://dotnet.microsoft.com)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3-1f6feb)](https://avaloniaui.net)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## NuGet Packages

End-user installs are typically `NativeWebView` plus the platform package for the target runtime. `NativeWebView.Dialog` and `NativeWebView.Auth` are optional facades, while `Core` and `Interop` are primarily composition units and transitive dependencies.

### Package Layout

| Package | Purpose |
| --- | --- |
| `NativeWebView` | Avalonia control facade API. |
| `NativeWebView.Core` | Shared contracts, controllers, feature model, and backend factory. |
| `NativeWebView.Dialog` | Dialog facade API. |
| `NativeWebView.Auth` | Web authentication broker facade API. |
| `NativeWebView.Interop` | Native handle contracts and structs. |
| `NativeWebView.Platform.Windows` | Windows backend registration and implementation. |
| `NativeWebView.Platform.macOS` | macOS backend registration and implementation. |
| `NativeWebView.Platform.Linux` | Linux backend registration and implementation. |
| `NativeWebView.Platform.iOS` | iOS backend registration and implementation. |
| `NativeWebView.Platform.Android` | Android backend registration and implementation. |
| `NativeWebView.Platform.Browser` | Browser backend registration and implementation. |

## Features

- `NativeWebView` control for embedded native browser surfaces inside Avalonia.
- `NativeWebDialog` facade for dialog and popup browser workflows.
- `WebAuthenticationBroker` facade for OAuth and interactive sign-in.
- Platform backends for Windows, macOS, Linux, iOS, Android, and Browser.
- Optional airspace-mitigation modes: `Embedded`, `GpuSurface`, and `Offscreen`.
- Diagnostics, capability reporting, render-frame export, integrity metadata, and smoke-testable sample apps.

## Platform Support Matrix

| Platform | Embedded WebView | GPU Surface Mode | Offscreen Mode | Dialog | Authentication Broker | Native Handles |
| --- | --- | --- | --- | --- | --- | --- |
| Windows | Yes | Yes | Yes | Yes | Yes | Yes |
| macOS | Yes | Yes | Yes | Yes | Yes | Yes |
| Linux | Yes | Yes | Yes | Yes | Yes | Yes |
| iOS | Yes | Yes | Yes | No | Yes | Yes |
| Android | Yes | Yes | Yes | No | Yes | Yes |
| Browser | Yes | Yes | Yes | No | Yes | Yes |

## Installation

Install the Avalonia control package and the platform backend that matches the runtime you ship:

```bash
dotnet add package NativeWebView
dotnet add package NativeWebView.Platform.Windows
```

Optional packages:

- `dotnet add package NativeWebView.Dialog`
- `dotnet add package NativeWebView.Auth`

Swap `NativeWebView.Platform.Windows` for `NativeWebView.Platform.macOS`, `NativeWebView.Platform.Linux`, `NativeWebView.Platform.iOS`, `NativeWebView.Platform.Android`, or `NativeWebView.Platform.Browser` as needed.

## Quick Start

```csharp
using NativeWebView.Controls;
using NativeWebView.Core;
using NativeWebView.Platform.Windows;

var factory = new NativeWebViewBackendFactory()
    .UseNativeWebViewWindows();

if (!factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Windows, out var backend))
{
    throw new InvalidOperationException("Windows backend is not available.");
}

using var webView = new NativeWebView(backend);
webView.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "./artifacts/example-userdata";
webView.InstanceConfiguration.EnvironmentOptions.CacheFolder = "./artifacts/example-cache";
webView.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
{
    Server = "http://localhost:8888"
};
webView.InstanceConfiguration.ControllerOptions.ProfileName = "example-profile";
await webView.InitializeAsync();
webView.RenderMode = NativeWebViewRenderMode.GpuSurface;
webView.RenderFramesPerSecond = 30;
webView.Navigate("https://example.com");
```

Each `NativeWebView` control instance keeps its own `InstanceConfiguration`, so multiple hosted views can carry different environment/controller defaults in the same process.

Per-instance proxy application is currently effective on macOS 14+ for the embedded `NativeWebView` control and `NativeWebDialog`. Windows, Linux, iOS, Android, and Browser continue to expose the configuration contract, but the current repo backends for those targets do not yet apply it at runtime. The macOS implementation currently supports explicit `http`, `https`, and `socks5` proxy servers plus bypass domains, and uses a dedicated persistent `WKWebsiteDataStore` identity derived from the instance configuration so proxied views do not fall back to private-browsing storage semantics. PAC (`AutoConfigUrl`) is not applied.

Use `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` to inspect the honest support status for each target. The current core package also exposes `NativeWebViewWindowsProxyArgumentsBuilder` and `NativeWebViewLinuxProxySettingsBuilder` so future or custom backends can translate shared proxy options into WebView2/WebKitGTK-specific configuration payloads without duplicating parsing logic.
Exact `UserDataFolder`/`CacheFolder`/`CookieDataFolder`/`SessionDataFolder` behavior remains backend-specific. In the current repo, non-macOS targets still expose these values as configuration contracts, and the macOS proxy path uses them as part of isolated store identity rather than direct physical directory mapping.

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
- `await webView.SaveRenderFrameAsync("artifacts/frame.png")`
- `await webView.SaveRenderFrameWithMetadataAsync("artifacts/frame.png", "artifacts/frame.json")`

Render sidecar metadata includes `FrameId`, `CapturedAtUtc`, `RenderMode`, `Origin`, `PixelDataLength`, and `PixelDataSha256`. Integrity verification requires matching `FormatVersion`.

## Samples

Run the desktop feature explorer:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug
```

Run the deterministic smoke matrix used by CI:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug -- --smoke
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
