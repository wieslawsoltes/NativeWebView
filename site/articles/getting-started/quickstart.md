---
title: "Quickstart"
---

# Quickstart

This walkthrough uses the default constructor and runtime auto-registration, which is the most direct path for an Avalonia application.

## 1. Place the Control in XAML

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:nwv="clr-namespace:NativeWebView.Controls;assembly=NativeWebView">
  <Grid>
    <nwv:NativeWebView x:Name="WebViewControl" />
  </Grid>
</Window>
```

## 2. Register the Current Platform and Initialize

```csharp
using NativeWebView.Controls;
using NativeWebView.Core;

NativeWebViewRuntime.EnsureCurrentPlatformRegistered();

await WebViewControl.InitializeAsync();
WebViewControl.RenderMode = NativeWebViewRenderMode.GpuSurface;
WebViewControl.RenderFramesPerSecond = 30;

if (!WebViewControl.SupportsRenderMode(WebViewControl.RenderMode))
{
    WebViewControl.RenderMode = NativeWebViewRenderMode.Embedded;
}

WebViewControl.Navigate("https://example.com");
```

If you host multiple views and need isolated browser state, assign per-instance defaults before initialization:

```csharp
WebViewControl.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "./artifacts/view-a/user-data";
WebViewControl.InstanceConfiguration.EnvironmentOptions.CacheFolder = "./artifacts/view-a/cache";
WebViewControl.InstanceConfiguration.EnvironmentOptions.CookieDataFolder = "./artifacts/view-a/cookies";
WebViewControl.InstanceConfiguration.EnvironmentOptions.SessionDataFolder = "./artifacts/view-a/session";
WebViewControl.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
{
    Server = "http://localhost:8888",
    BypassList = "localhost;127.0.0.1"
};
WebViewControl.InstanceConfiguration.ControllerOptions.ProfileName = "view-a";
```

Runtime note: per-instance proxy application is currently implemented on macOS 14+ for `NativeWebView` and `NativeWebDialog`. Other targets keep the configuration contract but do not yet apply proxy settings in the current repo implementation.

## 3. Validate Diagnostics Before Navigation

```csharp
var diagnostics = NativeWebViewRuntime.GetCurrentPlatformDiagnostics();
NativeWebViewDiagnosticsValidator.EnsureReady(diagnostics);
```

## 4. Capture a Rendered Frame When Using Composited Modes

```csharp
if (WebViewControl.RenderMode != NativeWebViewRenderMode.Embedded)
{
    var frame = await WebViewControl.CaptureRenderFrameAsync();
    Console.WriteLine($"Frame: id={frame?.FrameId}, origin={frame?.Origin}, mode={frame?.RenderMode}");
    var stats = WebViewControl.GetRenderStatisticsSnapshot();
    Console.WriteLine($"Capture stats: attempts={stats.CaptureAttemptCount}, success={stats.CaptureSuccessCount}, failures={stats.CaptureFailureCount}");
    await WebViewControl.SaveRenderFrameAsync("artifacts/nativewebview-frame.png");
    await WebViewControl.SaveRenderFrameWithMetadataAsync(
        "artifacts/nativewebview-frame-with-metadata.png",
        "artifacts/nativewebview-frame-with-metadata.json");

    if (frame is not null)
    {
        var sidecar = await NativeWebViewRenderFrameMetadataSerializer.ReadFromFileAsync(
            "artifacts/nativewebview-frame-with-metadata.json");
        var integrityOk = NativeWebViewRenderFrameMetadataSerializer.TryVerifyIntegrity(frame, sidecar, out var integrityError);
        Console.WriteLine($"Integrity ok: {integrityOk}, error={integrityError ?? "<null>"}");
    }

    WebViewControl.ResetRenderStatistics();
}
```

## 5. Optional Low-Level Backend Creation

If you want explicit backend composition instead of runtime auto-registration:

```csharp
using NativeWebView.Controls;
using NativeWebView.Core;
using NativeWebView.Platform.Windows;

var factory = new NativeWebViewBackendFactory()
    .UseNativeWebViewWindows();

if (!factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Windows, out var backend))
{
    throw new InvalidOperationException("Backend not registered.");
}

using var webView = new NativeWebView(backend);
webView.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "./artifacts/manual-backend-userdata";
webView.InstanceConfiguration.ControllerOptions.ProfileName = "manual-backend-profile";
await webView.InitializeAsync();
webView.Navigate("https://example.com");
```

## 6. Optional Diagnostics Report for CI and Local Gates

```bash
./scripts/run-platform-diagnostics-report.sh \
  --configuration Release \
  --platform all \
  --output artifacts/diagnostics/platform-diagnostics-report.json \
  --markdown-output artifacts/diagnostics/platform-diagnostics-report.md \
  --blocking-baseline ci/baselines/blocking-issues-baseline.txt \
  --comparison-markdown-output artifacts/diagnostics/blocking-regression.md \
  --comparison-json-output artifacts/diagnostics/blocking-regression.json \
  --comparison-evaluation-markdown-output artifacts/diagnostics/gate-evaluation.md \
  --require-baseline-sync \
  --allow-not-ready

./scripts/validate-diagnostics-exit-code-contract.sh \
  --configuration Release \
  --no-build \
  --output-dir artifacts/diagnostics/exit-code-contract \
  --baseline ci/baselines/blocking-issues-baseline.txt \
  --fingerprint-baseline ci/baselines/diagnostics-fingerprint-baseline.txt
```

## 7. Sample App

Run the desktop feature explorer:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug
```

## Next

- Read [NativeWebView](../controls/nativewebview.md) for the full control surface.
- Read [Render Modes](../rendering/render-modes.md) before enabling composited rendering in production.
- Run the [Sample Feature Explorer](sample-feature-explorer.md) to exercise the full feature set.
