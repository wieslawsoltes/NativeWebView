---
title: "Package Layout"
---

# Package Layout

End-user installs typically include `NativeWebView` plus the platform package for the target runtime. `NativeWebView.Dialog` and `NativeWebView.Auth` are optional facades, while `NativeWebView.Core` and `NativeWebView.Interop` are primarily composition units and transitive dependencies.

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
