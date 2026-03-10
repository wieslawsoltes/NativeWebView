---
title: "Lunet Docs Pipeline"
---

# Lunet Docs Pipeline

This repository uses Lunet for authored documentation and generated .NET API reference.

## Site Structure

- `site/config.scriban`: Lunet config and API generation setup.
- `site/menu.yml`: top-level navigation.
- `site/readme.md`: landing page.
- `site/articles/**`: authored documentation pages.
- `site/articles/**/menu.yml`: section sidebars.
- `site/images/**`: site assets.
- `site/.lunet/css/template-main.css`: precompiled template stylesheet.
- `site/.lunet/css/site-overrides.css`: project-specific styling.
- `site/.lunet/includes/_builtins/bundle.sbn-html`: bundle override used by the custom lite bundle.
- `site/.lunet/layouts/**`: API layout overrides.

## API Generation

The API reference is generated from the shipped projects under `src/` via `with api.dotnet` in `config.scriban`.

Current settings:

- output path: `/api`
- configuration: `Release`
- target framework: `net8.0`
- external Avalonia API mappings to `https://api-docs.avaloniaui.net/docs`

## Commands

From repository root:

```bash
./build-docs.sh
./check-docs.sh
./serve-docs.sh
```

PowerShell:

```powershell
./build-docs.ps1
./serve-docs.ps1
```

All commands run Lunet in `site/` and output to `site/.lunet/build/www`.

## CI Integration

- `ci.yml` builds the docs as part of the main validation pipeline.
- `docs.yml` publishes the Lunet output to GitHub Pages on pushes to `main` or `master`.
