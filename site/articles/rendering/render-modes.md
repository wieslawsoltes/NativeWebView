---
title: "Render Modes"
---

# Render Modes

NativeWebView supports three rendering strategies. The correct choice depends on whether you need maximum browser fidelity, composition with Avalonia overlays, frame capture, or reduced airspace issues.

## Embedded

`Embedded` hosts the native child view directly.

Use it when you need:

- the most native input and media behavior,
- lowest translation overhead,
- platform-default browser composition.

Trade-off:

- native airspace rules apply, so Avalonia content may not overlap the browser surface predictably.

## GPU Surface

`GpuSurface` captures frames from the backend and uploads them into an Avalonia-managed GPU surface.

Use it when you need:

- composited hosting inside Avalonia,
- reduced airspace artifacts,
- frame capture while staying close to GPU-backed presentation.

Trade-off:

- fidelity depends on platform capture support,
- some hardware-accelerated media paths may require native passthrough behavior instead of captured composition.

## Offscreen

`Offscreen` captures the browser surface for full managed composition by Avalonia.

Use it when you need:

- overlay content above the browser,
- fully managed layout and capture flows,
- render-frame export and diagnostics.

Trade-off:

- the backend must provide a working frame source,
- input and media behavior are more sensitive to platform implementation details than in `Embedded` mode.

## Capability Checks

Always check support before switching:

```csharp
if (!WebViewControl.SupportsRenderMode(NativeWebViewRenderMode.Offscreen))
{
    WebViewControl.RenderMode = NativeWebViewRenderMode.Embedded;
}
```

## Frame Capture APIs

Composited modes expose:

- `CaptureRenderFrameAsync`
- `SaveRenderFrameAsync`
- `SaveRenderFrameWithMetadataAsync`
- `RenderStatistics`
- `ResetRenderStatistics`

## macOS Composited Passthrough

The control exposes `SetCompositedPassthroughOverride(bool?)` for macOS-specific composited passthrough decisions in cases where video playback or overlay behavior requires manual override.

Use this only when the default host detection does not match your scenario.

## Diagnostics Signals

Inspect these values when capture paths are not behaving as expected:

- `IsUsingSyntheticFrameSource`
- `RenderDiagnosticsMessage`
- `RenderStatistics.LastFrameOrigin`
- `RenderStatistics.LastFailureMessage`

## Related

- [NativeWebView](../controls/nativewebview.md)
- [Native Handle Interop](native-handle-interop.md)
