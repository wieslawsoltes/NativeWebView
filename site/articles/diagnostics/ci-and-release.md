---
title: "CI and Release"
---

# CI and Release

## Pull Request CI

Workflow: `.github/workflows/ci.yml`

Runs on:

- `ubuntu-latest`
- `macos-latest`
- `windows-latest`

Stages:

1. Quality gate with `dotnet format --verify-no-changes --severity warn`.
2. Changelog fragment validation.
3. Docs build with Lunet via `./check-docs.sh`.
4. Restore, build, and test on Windows, macOS, and Linux.
5. Diagnostics exit-code contract validation.
6. Desktop and mobile/browser sample smoke runs.
7. Platform diagnostics JSON and markdown generation.
8. Baseline regression and baseline-sync enforcement.
9. Diagnostics, regression, gate-evaluation, and fingerprint summaries appended to the workflow summary.
10. Machine-readable diagnostics artifacts published for release triage and policy auditing.
11. NuGet packaging and package validation.
12. Artifact publication for tests, docs, diagnostics, and packages.

Additional details:

- Smoke scripts enable `NATIVEWEBVIEW_DIAGNOSTICS_REQUIRE_READY=1` to fail on blocking prerequisite issues.
- Exit-code contract validation enforces fingerprint baselines from `ci/baselines/diagnostics-fingerprint-baseline.txt`.
- Package validation checks `.nupkg`, `.snupkg`, nuspec metadata, README/license packing, and expected inter-package dependencies.

## Docs Publishing

Workflow: `.github/workflows/docs.yml`

Stages:

1. Bootstrap the pinned Lunet package from NuGet.
2. Build the Lunet site via `./build-docs.sh`.
3. Publish `site/.lunet/build/www` to GitHub Pages.

The workflow is push-driven and uses the same Lunet shell script locally and in CI so authored docs and API generation stay on one path.

## Tag Release

Workflow: `.github/workflows/release.yml`

Trigger:

- Push tag `v*` (for example `v12.0.0`)

Stages:

1. Resolve version from `v*` tag.
2. Restore, build, and test in `Release`.
3. Run diagnostics exit-code contract validation and smoke checks.
4. Generate diagnostics reports and enforce baseline sync.
5. Pack and validate NuGet packages, symbol packages, and the Windows package consumer.
6. Upload package and diagnostics artifacts and publish the package validation summary.
7. Exchange GitHub's OIDC token for a short-lived NuGet.org API key in the `nuget` environment, then push the packages and symbols.
8. Create the GitHub release and attach the package archives.

The `nuget` GitHub environment allows `v*` tags and sets `NUGET_USER` to the NuGet.org package owner's username. Before pushing a release tag, add a GitHub Actions policy on NuGet.org. [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

| NuGet.org field | Value |
| --- | --- |
| Policy Name | `NativeWebView Release` |
| Package Owner | `wieslawsoltes` |
| CI/CD Provider | GitHub Actions |
| Repository owner | `wieslawsoltes` |
| Repository | `NativeWebView` |
| Workflow file | `release.yml` |
| Environment | `nuget` |
| Scope | Push only new package versions |
| Glob Patterns and Packages | `NativeWebView` and `NativeWebView.*` on separate lines |

## Extended Validation Workflow

Workflow: `.github/workflows/extended-validation.yml`

Trigger:

- Manual (`workflow_dispatch`)
- Weekly schedule (`cron`)

Stages:

1. Browser docs validation via `scripts/run-browser-playwright-smoke.sh`.
2. iOS mobile/browser contract smoke with simulator boot on `macos-latest`.
3. Android mobile/browser contract smoke inside an emulator on `ubuntu-latest`.
4. Upload Playwright report, iOS simulator logs, and Android logcat artifacts.

## Local Docs Commands

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

## Local Packaging and Release Validation

Run the embedded browser regression suites before packaging:

```bash
dotnet test tests/NativeWebView.Core.Tests/NativeWebView.Core.Tests.csproj
node --test tests/macos-web-message-bridge.test.mjs
```

The Node tests execute the production macOS bridge bootstrap and exercise message
direction, listener cleanup, session isolation and JSON property preservation. They
require Node.js and are a separate local command; they are not implicitly run by
`dotnet test`.

For authentication consumers, verify the final package on Windows and macOS with
redirect interception, interactive sign-in/MFA and federation, application-provided
autofill, cancellation, process failure, resize/rehosting and profile cleanup. Check
that disabled context menus and downloads remain disabled. Test after updating all
consumed NativeWebView packages to the same release version. Unit tests and a source
build alone do not establish native authentication compatibility.

```bash
dotnet build NativeWebView.sln -c Release
./scripts/run-platform-diagnostics-report.sh --configuration Release --no-build --platform all --output artifacts/diagnostics/platform-diagnostics-report.json --markdown-output artifacts/diagnostics/platform-diagnostics-report.md --blocking-baseline ci/baselines/blocking-issues-baseline.txt --blocking-baseline-output artifacts/diagnostics/current-blocking-baseline.txt --comparison-markdown-output artifacts/diagnostics/blocking-regression.md --comparison-json-output artifacts/diagnostics/blocking-regression.json --comparison-evaluation-markdown-output artifacts/diagnostics/gate-evaluation.md --require-baseline-sync --allow-not-ready
./scripts/validate-diagnostics-exit-code-contract.sh --configuration Release --no-build --output-dir artifacts/diagnostics/exit-code-contract --baseline ci/baselines/blocking-issues-baseline.txt --fingerprint-baseline ci/baselines/diagnostics-fingerprint-baseline.txt
dotnet pack NativeWebView.sln -c Release --no-build -o artifacts/packages
bash ./scripts/validate-nuget-packages.sh --package-dir artifacts/packages --markdown-output artifacts/packages/package-validation.md
```

Refresh baselines when intentional prerequisite or fingerprint changes are accepted:

```bash
./scripts/update-blocking-baseline.sh --configuration Release --platform all --output ci/baselines/blocking-issues-baseline.txt
./scripts/update-diagnostics-fingerprint-baseline.sh --configuration Release --output ci/baselines/diagnostics-fingerprint-baseline.txt
```
