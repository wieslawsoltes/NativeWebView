---
title: "Installation"
---

# Installation

## Core Package Setup

Install the Avalonia control package plus the platform package that matches the runtime you ship:

```bash
dotnet add package NativeWebView
dotnet add package NativeWebView.Platform.Windows
```

Replace `NativeWebView.Platform.Windows` with `NativeWebView.Platform.macOS`, `NativeWebView.Platform.Linux`, `NativeWebView.Platform.iOS`, `NativeWebView.Platform.Android`, or `NativeWebView.Platform.Browser` as needed.

Optional facades:

```bash
dotnet add package NativeWebView.Dialog
dotnet add package NativeWebView.Auth
```

## Package Layout

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

## Backend Registration

Register the backend in application startup or composition root code:

```csharp
using NativeWebView.Core;
using NativeWebView.Platform.Windows;

var factory = new NativeWebViewBackendFactory();
factory.UseNativeWebViewWindows();
```

If you rely on default constructors, runtime auto-registration is available:

```csharp
NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
```

## Readiness Validation

Validate prerequisites before creating the first control:

```csharp
var diagnostics = factory.GetPlatformDiagnosticsOrDefault(NativeWebViewPlatform.Windows);
NativeWebViewDiagnosticsValidator.EnsureReady(diagnostics);
```

This is the recommended startup guard in both samples and CI.

## Avalonia Integration Notes

Add the XML namespace in XAML:

```xml
xmlns:nwv="clr-namespace:NativeWebView.Controls;assembly=NativeWebView"
```

Then place the control in layout markup:

```xml
<nwv:NativeWebView x:Name="WebViewControl" />
```

The default constructor uses `NativeWebViewRuntime` to resolve the current platform backend.

## Next

- Continue with [Quickstart](quickstart.md).
- Review [Platform Notes](../platforms/readme.md) if your package selection is platform-specific.
- Review [Platform Prerequisites](../diagnostics/platform-prerequisites.md) if startup validation is failing.
