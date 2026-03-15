# Issue 2 Per-Instance Configuration Checklist

## Issue Summary

GitHub issue `#2` asks whether multiple hosted `NativeWebView` instances can carry different browser configuration on a per-instance basis, specifically for:

- Proxy configuration
- Cache directories
- Cookie directories
- Session directories

## Analysis

- The existing API already exposed per-backend initialization events through `CoreWebView2EnvironmentRequested` and `CoreWebView2ControllerOptionsRequested`.
- Those events were instance-scoped, but the public surface did not provide a first-class per-instance configuration object and the option bag did not include proxy/cache/cookie/session paths.
- The control also lacked a deterministic way to seed per-instance defaults before user event handlers ran.

## Implementation Plan

- Add a first-class `NativeWebViewInstanceConfiguration` model that owns environment and controller defaults for a single control instance.
- Extend `NativeWebViewEnvironmentOptions` with proxy, cache, cookie, and session directory settings.
- Deep-clone configuration state when assigning it to a control so multiple controls can start from the same template without sharing mutable state.
- Apply the control configuration before public option-request handlers run so apps can still customize or override seeded values.
- Add regression tests that prove two `NativeWebView` controls can initialize with different per-instance values.
- Update README, docs, sample defaults, and changelog text to advertise the new API.

## Exit Criteria

- `NativeWebView` exposes an explicit per-instance configuration surface.
- Proxy/storage fields are available in environment options.
- Two controls initialized from the same template can diverge safely.
- Docs show how to configure unique browser state per hosted instance.
