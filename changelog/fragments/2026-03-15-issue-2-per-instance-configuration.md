[Added] Added `NativeWebView.InstanceConfiguration` so each hosted control instance can seed its own environment and controller defaults before initialization.
[Added] Extended `NativeWebViewEnvironmentOptions` with proxy, cache, cookie, and session directory settings for per-instance browser state configuration.
[Added] Added regression coverage proving multiple `NativeWebView` controls can initialize from a shared template without leaking configuration across instances.
[Docs] Updated README, quickstart, rendering/options docs, and the desktop sample to show per-instance configuration usage.
