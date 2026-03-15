# Issue 2 Additional Platform Proxy Support Checklist

## Goal

Evaluate whether the new per-instance proxy configuration model can be extended beyond the current macOS runtime implementation, then implement every remaining piece that is feasible in the current repository without making false runtime-support claims.

## Analysis

- macOS already has a real native runtime path in this repo, so it is the only platform where per-instance proxy settings can currently be applied end-to-end.
- Windows and Linux both have official runtime hooks for proxy configuration, but the current repo backends are still stub implementations and do not create real WebView2/WebKitGTK views.
- iOS has the same WebKit proxy API family as macOS (`WKWebsiteDataStore.proxyConfigurations` on iOS 17+), but the current repo does not yet have a real iOS hosting/backend path.
- AndroidX WebKit exposes proxy override through `ProxyController`, but the official API is app-wide rather than per-`WebView`.
- Browser-hosted targets run inside the host browser networking stack and do not expose per-instance engine proxy control through the current implementation.

## Implementation Plan

- Add a public support-matrix API that reports the real platform capability and the current repo support level separately.
- Add a Windows translation helper that converts shared proxy options into WebView2/Chromium `AdditionalBrowserArguments`.
- Add a Linux translation helper that converts shared proxy options into a WebKitGTK-style default proxy URI plus ignore-host list.
- Add tests for support-matrix reporting and the new platform translation helpers.
- Update docs to point consumers at the support-matrix API and clarify which helper code is groundwork versus active runtime integration.

## Exit Criteria

- Consumers can inspect proxy support in code via a dedicated API instead of inferring from docs or platform names.
- Windows and Linux have reusable, tested translation helpers for future/custom runtime integrations.
- Docs clearly distinguish official platform capability from what this repo applies at runtime today.
