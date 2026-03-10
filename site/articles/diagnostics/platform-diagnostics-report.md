---
title: "Platform Diagnostics Report"
---

# Platform Diagnostics Report

Use the diagnostics reporter to generate a JSON summary for one or more platforms and optionally fail when blocking issues are detected.

## CLI Sample

Project:

- `samples/NativeWebView.Sample.Diagnostics`

Example:

```bash
dotnet run --project samples/NativeWebView.Sample.Diagnostics/NativeWebView.Sample.Diagnostics.csproj -- \
  --platform all \
  --output artifacts/diagnostics/platform-diagnostics-report.json \
  --require-ready
```

## Options

- `--platform <value>`: `all` or a comma-separated subset of `windows,macos,linux,ios,android,browser`.
- `--output <path>`: output JSON file path.
- `--markdown-output <path>`: optional markdown summary output path.
- `--blocking-baseline <path>`: optional baseline file (`<Platform>|<IssueCode>`).
- `--blocking-baseline-output <path>`: optional path to write current baseline entries.
- `--comparison-markdown-output <path>`: optional markdown output for baseline comparison.
- `--comparison-json-output <path>`: optional JSON output for baseline comparison.
- `--comparison-evaluation-markdown-output <path>`: optional markdown output for gate evaluation.
- `--require-ready`: enable readiness gate.
- `--warnings-as-errors`: treat warnings as blocking.
- `--allow-regression`: do not fail when new blocking issues are found.
- `--require-baseline-sync`: fail when the baseline contains resolved or stale entries.

## Exit Code Contract

- `0`: no enabled gates failed.
- `10`: readiness gate failed.
- `11`: regression gate failed.
- `12`: baseline-sync gate failed.
- `13`: multiple enabled gates failed in one run.

## Wrapper Script

Use the repository wrapper script for CI and local consistency:

```bash
./scripts/run-platform-diagnostics-report.sh \
  --configuration Release \
  --no-build \
  --platform all \
  --output artifacts/diagnostics/platform-diagnostics-report.json \
  --markdown-output artifacts/diagnostics/platform-diagnostics-report.md \
  --blocking-baseline ci/baselines/blocking-issues-baseline.txt \
  --blocking-baseline-output artifacts/diagnostics/current-blocking-baseline.txt \
  --comparison-markdown-output artifacts/diagnostics/blocking-regression.md \
  --comparison-json-output artifacts/diagnostics/blocking-regression.json \
  --comparison-evaluation-markdown-output artifacts/diagnostics/gate-evaluation.md \
  --require-baseline-sync \
  --allow-not-ready
```

Additional script behavior:

- `--allow-not-ready`: generate reports without failing when blocking issues are present.
- By default, `run-platform-diagnostics-report.sh` enforces readiness.
- By default, baseline comparison failures fail the command unless `--allow-regression` is set.
- `--require-baseline-sync` enforces baseline hygiene by failing when stale resolved entries remain in the baseline.

## Output Artifacts

The report pipeline produces:

- JSON and markdown summary reports,
- blocking regression comparison reports,
- gate evaluation markdown and JSON,
- exit-code contract conformance summaries,
- deterministic evaluation fingerprints for baseline drift detection.

## Report Shape

Top-level fields:

- `generatedAtUtc`
- `warningsAsErrors`
- `isReady`
- `issueCount`
- `warningCount`
- `errorCount`
- `blockingIssueCount`
- `platforms`

Each platform entry includes:

- `platform`
- `providerName`
- `providerRegistered`
- `isReady`
- `issueCount`
- `warningCount`
- `errorCount`
- `blockingIssueCount`
- `issues[]` with `code`, `severity`, `message`, and `recommendation`

Markdown output includes:

- overall readiness and counts,
- platform summary table,
- per-platform issue sections.

Blocking regression markdown output includes:

- baseline and current blocking issue counts,
- new blocking issues,
- resolved blocking issues,
- regression and stale-baseline flags.

Blocking regression JSON output includes:

- policy flags (`requireReady`, `failOnRegression`, `requireBaselineSync`),
- gate outcomes (`wouldFailRequireReady`, `wouldFailRegressionGate`, `wouldFailBaselineSyncGate`),
- final `effectiveExitCode`,
- deterministic `fingerprint` and `fingerprintVersion`,
- structured `gateFailures[]`,
- embedded comparison details when baseline comparison is enabled.

Gate evaluation markdown output includes:

- policy flags and readiness/comparison state,
- effective exit code and primary failure classification,
- deterministic evaluation fingerprint,
- optional baseline comparison snapshot counts,
- failing gate descriptions and recommendations.

## Baseline Refresh Helper

Use the helper script to regenerate the baseline from current diagnostics:

```bash
./scripts/update-blocking-baseline.sh \
  --configuration Release \
  --platform all \
  --output ci/baselines/blocking-issues-baseline.txt
```

Optional strict regeneration:

```bash
./scripts/update-blocking-baseline.sh --warnings-as-errors
```

## Exit Code Contract Validation Script

Use the dedicated validation script to exercise all gate outcomes (`0`, `10`, `11`, `12`, `13`):

```bash
./scripts/validate-diagnostics-exit-code-contract.sh \
  --configuration Release \
  --no-build \
  --output-dir artifacts/diagnostics/exit-code-contract \
  --baseline ci/baselines/blocking-issues-baseline.txt
```

Optional fingerprint baseline gate:

```bash
./scripts/validate-diagnostics-exit-code-contract.sh \
  --configuration Release \
  --no-build \
  --output-dir artifacts/diagnostics/exit-code-contract \
  --baseline ci/baselines/blocking-issues-baseline.txt \
  --fingerprint-baseline ci/baselines/diagnostics-fingerprint-baseline.txt
```

Outputs include:

- `exit-code-contract-summary.md`
- `exit-code-contract-summary.csv`
- `exit-code-contract-summary.json`
- scenario logs and generated evaluation or report JSON files
- per-scenario gate evaluation markdown files
- `fingerprint-current.txt`
- `fingerprint-baseline-comparison.md` and `fingerprint-baseline-comparison.json` when fingerprint gating is enabled

Refresh the fingerprint baseline when contract changes are intentional:

```bash
./scripts/update-diagnostics-fingerprint-baseline.sh \
  --configuration Release \
  --output ci/baselines/diagnostics-fingerprint-baseline.txt
```

## CI and Release Usage

- CI publishes diagnostics, regression, gate evaluation, and fingerprint comparison markdown into the workflow summary.
- CI and release compare blocking issues against `ci/baselines/blocking-issues-baseline.txt` and enforce baseline-sync hygiene.
- CI and release publish machine-readable regression evaluation JSON artifacts for policy and gate auditing.
- Release appends diagnostics, regression, gate-evaluation, and exit-code conformance summaries to release notes and attaches the raw artifacts to the GitHub release.
