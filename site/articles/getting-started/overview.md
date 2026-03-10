---
title: "Getting Started with NativeWebView"
---

# Getting Started with NativeWebView

NativeWebView is split into a small set of focused packages so you can compose only the browser surface you need:

- `NativeWebView`: Avalonia `NativeControlHost` surface for embedding the native browser view.
- `NativeWebView.Core`: controller layer, diagnostics, capability model, and backend abstractions.
- `NativeWebView.Dialog`: dialog/window facade for desktop browser windows.
- `NativeWebView.Auth`: authentication broker facade for OAuth and sign-in flows.
- `NativeWebView.Interop`: native handle contracts and interop structs.
- `NativeWebView.Platform.*`: platform registrations and backend implementations.

## What You Will Build

By the end of this section you will have:

- An Avalonia window hosting `NativeWebView`.
- Platform backend registration for the current runtime.
- Runtime readiness checks before first navigation.
- A working choice between `Embedded`, `GpuSurface`, and `Offscreen` render modes.

## Recommended Learning Path

1. [Installation](installation.md)
2. [Quickstart](quickstart.md)
3. [Render Modes](../rendering/render-modes.md)
4. [Platform Notes](../platforms/readme.md)
5. [Platform Prerequisites](../diagnostics/platform-prerequisites.md)

## Pick the Right Integration Path

Choose the primary surface first:

- Use [`NativeWebView`](../controls/nativewebview.md) when the browser must live inside an Avalonia layout.
- Use [`NativeWebDialog`](../controls/nativewebdialog.md) when the browser should live in a separate desktop window.
- Use [`WebAuthenticationBroker`](../controls/webauthenticationbroker.md) when the main goal is an auth callback flow.

Choose the render mode next:

- `Embedded`: best native fidelity and input behavior.
- `GpuSurface`: composited capture path for reduced airspace issues while staying GPU-backed.
- `Offscreen`: fully managed composition path for overlays, captures, and host-controlled layering.

## Operational Model

NativeWebView assumes startup validation is part of the application contract:

1. Register the current platform backend.
2. Read diagnostics for the target platform.
3. Fail fast on blocking issues.
4. Create and initialize the control or facade.
5. Enable optional CI/reporting scripts when shipping packages.

## Next

- Move to [Installation](installation.md) to choose packages.
- Jump to [Quickstart](quickstart.md) if the repository is already referenced locally.
- Use [Sample Feature Explorer](sample-feature-explorer.md) to validate capabilities end to end.
