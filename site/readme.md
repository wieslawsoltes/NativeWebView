---
title: "NativeWebView"
layout: simple
og_type: website
---

<div class="nwv-hero">
  <div class="nwv-eyebrow"><i class="bi bi-globe2" aria-hidden="true"></i> Avalonia Native Browser Stack</div>
  <h1>NativeWebView</h1>

  <p class="lead"><strong>NativeWebView</strong> gives Avalonia applications a consistent browser surface while staying on top of platform-native engines such as WebView2, WKWebView, WebKitGTK, mobile WebView, and browser host integrations. It supports embedded hosting, GPU-surface rendering, offscreen composition, dialogs, authentication broker flows, diagnostics, and ship-ready NuGet packaging.</p>

  <div class="nwv-hero-actions">
    <a class="btn btn-primary btn-lg" href="articles/getting-started/overview"><i class="bi bi-rocket-takeoff" aria-hidden="true"></i> Start Getting Started</a>
    <a class="btn btn-outline-secondary btn-lg" href="articles/rendering/render-modes"><i class="bi bi-layers" aria-hidden="true"></i> Explore Render Modes</a>
    <a class="btn btn-outline-secondary btn-lg" href="api"><i class="bi bi-braces" aria-hidden="true"></i> Browse API</a>
    <a class="btn btn-outline-secondary btn-lg" href="https://github.com/wieslawsoltes/NativeWebVIew"><i class="bi bi-github" aria-hidden="true"></i> GitHub Repository</a>
  </div>
</div>

## Start Here

<div class="nwv-link-grid">
  <a class="nwv-link-card" href="articles/getting-started/overview">
    <span class="nwv-link-card-title"><i class="bi bi-signpost-split" aria-hidden="true"></i> Getting Started Overview</span>
    <p>Understand package roles, backend registration, diagnostics, and the main integration path.</p>
  </a>
  <a class="nwv-link-card" href="articles/getting-started/installation">
    <span class="nwv-link-card-title"><i class="bi bi-download" aria-hidden="true"></i> Installation</span>
    <p>Pick the correct platform packages, optional facades, and startup validation flow.</p>
  </a>
  <a class="nwv-link-card" href="articles/getting-started/quickstart">
    <span class="nwv-link-card-title"><i class="bi bi-play-circle" aria-hidden="true"></i> Quickstart</span>
    <p>Create the control in XAML, initialize it, switch render modes, and navigate.</p>
  </a>
  <a class="nwv-link-card" href="articles/getting-started/sample-feature-explorer">
    <span class="nwv-link-card-title"><i class="bi bi-window-desktop" aria-hidden="true"></i> Sample Feature Explorer</span>
    <p>Run the desktop sample that exercises webview, dialog, auth, rendering, and diagnostics.</p>
  </a>
</div>

## Documentation Sections

<div class="nwv-link-grid nwv-link-grid--wide">
  <a class="nwv-link-card" href="articles/controls">
    <span class="nwv-link-card-title"><i class="bi bi-app-indicator" aria-hidden="true"></i> Controls and Facades</span>
    <p><code>NativeWebView</code>, <code>NativeWebDialog</code>, and <code>WebAuthenticationBroker</code> usage, capabilities, and events.</p>
  </a>
  <a class="nwv-link-card" href="articles/rendering">
    <span class="nwv-link-card-title"><i class="bi bi-layers-half" aria-hidden="true"></i> Rendering and Interop</span>
    <p>Embedded, GPU-surface, offscreen, environment options, and native handle access.</p>
  </a>
  <a class="nwv-link-card" href="articles/platforms">
    <span class="nwv-link-card-title"><i class="bi bi-pc-display-horizontal" aria-hidden="true"></i> Platforms</span>
    <p>Windows, macOS, Linux, iOS, Android, and Browser backend notes with registration snippets.</p>
  </a>
  <a class="nwv-link-card" href="articles/diagnostics">
    <span class="nwv-link-card-title"><i class="bi bi-heart-pulse" aria-hidden="true"></i> Diagnostics and Operations</span>
    <p>Runtime readiness, report generation, CI gates, release validation, and troubleshooting data.</p>
  </a>
  <a class="nwv-link-card" href="articles/reference">
    <span class="nwv-link-card-title"><i class="bi bi-collection" aria-hidden="true"></i> Reference</span>
    <p>Package layout, platform support matrix, docs pipeline, and licensing.</p>
  </a>
  <a class="nwv-link-card" href="api">
    <span class="nwv-link-card-title"><i class="bi bi-braces-asterisk" aria-hidden="true"></i> API Documentation</span>
    <p>Generated .NET API pages for the core control, facades, abstractions, and platform packages.</p>
  </a>
</div>

## Highlights

- Native engine integration instead of shipping a bundled Chromium runtime.
- Three render strategies: `Embedded`, `GpuSurface`, and `Offscreen`.
- Desktop dialog hosting and cross-platform authentication broker abstraction.
- Diagnostics and CI gate tooling for readiness, regressions, and release baselines.
- Native-handle interop for advanced host integrations.

## Repository

- Source code and issues: [github.com/wieslawsoltes/NativeWebVIew](https://github.com/wieslawsoltes/NativeWebVIew)
- Published documentation: [wieslawsoltes.github.io/NativeWebVIew](https://wieslawsoltes.github.io/NativeWebVIew)
