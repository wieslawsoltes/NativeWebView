# Issue 2 Per-Instance Proxy Configuration Checklist

## Issue Summary

Follow-up to GitHub issue `#2`: determine whether per-instance proxy configuration can be applied for hosted browser instances across the supported platform packages, then implement the real runtime paths that are feasible in the current repo.

## Analysis

- macOS and iOS WebKit expose `WKWebsiteDataStore.proxyConfigurations`, but this repo currently has a real native runtime path only on macOS.
- Windows WebView2 and Linux WebKitGTK have official proxy configuration hooks, but the current repo backends for those targets are still stubs and do not create real native webview environments.
- Android’s official proxy override is app-wide rather than per-`WebView`.
- Browser-hosted targets do not expose per-instance engine proxy control through the current implementation.

## Implementation Plan

- Add a parser/resolver for explicit proxy server settings so host, port, credentials, bypass domains, and PAC input are validated consistently.
- Introduce a dedicated feature flag so runtime proxy support can be surfaced through `Features`.
- Implement real per-instance proxy application on macOS embedded `NativeWebView` and `NativeWebDialog` using dedicated `WKWebsiteDataStore` instances with `proxyConfigurations`.
- Forward instance configuration into dialog backends so the macOS dialog path can apply proxy settings before native window/webview creation.
- Add tests for proxy parsing, feature support, and dialog configuration forwarding.
- Update README and docs with the actual support matrix so stub backends are called out explicitly.

## Exit Criteria

- macOS 14+ embedded and dialog flows apply explicit per-instance proxy servers and bypass domains at runtime.
- The support matrix is explicit for Windows, Linux, iOS, Android, and Browser.
- Tests cover parsing, feature flags, and dialog configuration plumbing.
