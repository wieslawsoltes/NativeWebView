---
title: "Sample Feature Explorer"
---

# Sample Feature Explorer

The desktop sample demonstrates the full surface area of the library in an Avalonia application.

## Desktop Feature Explorer

Run the desktop sample:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug
```

The sample exercises:

- `NativeWebView` navigation, messaging, printing, state, and native handles.
- Render mode switching between `Embedded`, `GpuSurface`, and `Offscreen`.
- Managed overlay testing for airspace verification.
- `NativeWebDialog` actions on desktop backends.
- `WebAuthenticationBroker` flows.
- Runtime diagnostics and feature summaries.

## Deterministic Smoke Mode

Run the smoke path used by CI:

```bash
dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c Debug -- --smoke
```

This path validates backend contracts and capability coverage with a predictable command-line flow.

## Diagnostics Sample

Generate a readiness report from the diagnostics sample:

```bash
dotnet run --project samples/NativeWebView.Sample.Diagnostics/NativeWebView.Sample.Diagnostics.csproj -- \
  --platform all \
  --output artifacts/diagnostics/platform-diagnostics-report.json \
  --markdown-output artifacts/diagnostics/platform-diagnostics-report.md
```

## Mobile and Browser Sample

For mobile or browser smoke validation:

```bash
./scripts/run-mobile-browser-sample-smoke.sh --configuration Release --platform all
```

## When to Use the Sample

Use the feature explorer when you need to verify:

- platform capability coverage before packaging,
- render mode behavior on a specific host,
- interop/native handle exposure,
- diagnostic baseline changes during CI or release work.
