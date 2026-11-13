#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

configuration="Release"
platform="all"
output="artifacts/diagnostics/platform-diagnostics-report.json"
markdown_output=""
blocking_baseline=""
blocking_baseline_output=""
comparison_markdown_output=""
comparison_json_output=""
comparison_evaluation_markdown_output=""
no_build=false
require_ready=true
warnings_as_errors=false
fail_on_regression=true
require_baseline_sync=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --platform)
      platform="$2"
      shift 2
      ;;
    --output)
      output="$2"
      shift 2
      ;;
    --markdown-output)
      markdown_output="$2"
      shift 2
      ;;
    --blocking-baseline)
      blocking_baseline="$2"
      shift 2
      ;;
    --blocking-baseline-output)
      blocking_baseline_output="$2"
      shift 2
      ;;
    --comparison-markdown-output)
      comparison_markdown_output="$2"
      shift 2
      ;;
    --comparison-json-output)
      comparison_json_output="$2"
      shift 2
      ;;
    --comparison-evaluation-markdown-output)
      comparison_evaluation_markdown_output="$2"
      shift 2
      ;;
    --no-build)
      no_build=true
      shift
      ;;
    --require-ready)
      require_ready=true
      shift
      ;;
    --allow-not-ready)
      require_ready=false
      shift
      ;;
    --warnings-as-errors)
      warnings_as_errors=true
      shift
      ;;
    --allow-regression)
      fail_on_regression=false
      shift
      ;;
    --require-baseline-sync)
      require_baseline_sync=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ "$require_baseline_sync" == "true" && -z "$blocking_baseline" ]]; then
  echo "--require-baseline-sync requires --blocking-baseline" >&2
  exit 1
fi

if [[ -n "$comparison_markdown_output" && -z "$blocking_baseline" ]]; then
  echo "--comparison-markdown-output requires --blocking-baseline" >&2
  exit 1
fi

if [[ -n "$comparison_json_output" && -z "$blocking_baseline" ]]; then
  echo "--comparison-json-output requires --blocking-baseline" >&2
  exit 1
fi

if [[ -n "$blocking_baseline" && ! -f "$blocking_baseline" ]]; then
  echo "Blocking diagnostics baseline file was not found: $blocking_baseline" >&2
  exit 1
fi

mkdir -p "$(dirname "$output")"
if [[ -n "$markdown_output" ]]; then
  mkdir -p "$(dirname "$markdown_output")"
fi
if [[ -n "$blocking_baseline_output" ]]; then
  mkdir -p "$(dirname "$blocking_baseline_output")"
fi
if [[ -n "$comparison_markdown_output" ]]; then
  mkdir -p "$(dirname "$comparison_markdown_output")"
fi
if [[ -n "$comparison_json_output" ]]; then
  mkdir -p "$(dirname "$comparison_json_output")"
fi
if [[ -n "$comparison_evaluation_markdown_output" ]]; then
  mkdir -p "$(dirname "$comparison_evaluation_markdown_output")"
fi

cmd=(dotnet run --project samples/NativeWebView.Sample.Diagnostics/NativeWebView.Sample.Diagnostics.csproj -c "$configuration" --framework "${NATIVEWEBVIEW_FRAMEWORK:-net11.0}")
if [[ "$no_build" == "true" ]]; then
  cmd+=(--no-build)
fi

cmd+=(-- --platform "$platform" --output "$output")
if [[ -n "$markdown_output" ]]; then
  cmd+=(--markdown-output "$markdown_output")
fi
if [[ -n "$blocking_baseline" ]]; then
  cmd+=(--blocking-baseline "$blocking_baseline")
fi
if [[ -n "$blocking_baseline_output" ]]; then
  cmd+=(--blocking-baseline-output "$blocking_baseline_output")
fi
if [[ -n "$comparison_markdown_output" ]]; then
  cmd+=(--comparison-markdown-output "$comparison_markdown_output")
fi
if [[ -n "$comparison_json_output" ]]; then
  cmd+=(--comparison-json-output "$comparison_json_output")
fi
if [[ -n "$comparison_evaluation_markdown_output" ]]; then
  cmd+=(--comparison-evaluation-markdown-output "$comparison_evaluation_markdown_output")
fi
if [[ "$require_ready" == "true" ]]; then
  cmd+=(--require-ready)
fi
if [[ "$warnings_as_errors" == "true" ]]; then
  cmd+=(--warnings-as-errors)
fi
if [[ "$fail_on_regression" == "false" ]]; then
  cmd+=(--allow-regression)
fi
if [[ "$require_baseline_sync" == "true" ]]; then
  cmd+=(--require-baseline-sync)
fi

set +e
run_output="$("${cmd[@]}" 2>&1)"
exit_code=$?
set -e
printf '%s\n' "$run_output"

if [[ $exit_code -ne 0 ]]; then
  echo "Diagnostics report command failed with exit code $exit_code." >&2
  exit "$exit_code"
fi

if [[ ! -f "$output" ]]; then
  echo "Diagnostics report output was not created: $output" >&2
  exit 1
fi

if ! grep -q '"platforms"' "$output"; then
  echo "Diagnostics report output does not contain platforms section: $output" >&2
  exit 1
fi

if [[ -n "$markdown_output" ]]; then
  if [[ ! -f "$markdown_output" ]]; then
    echo "Diagnostics markdown summary output was not created: $markdown_output" >&2
    exit 1
  fi

  if ! grep -q "| Platform | Ready | Provider | Registered | Warnings | Errors | Blocking |" "$markdown_output"; then
    echo "Diagnostics markdown summary output does not include expected platform table: $markdown_output" >&2
    exit 1
  fi
fi

if [[ -n "$blocking_baseline_output" ]]; then
  if [[ ! -f "$blocking_baseline_output" ]]; then
    echo "Blocking diagnostics baseline output was not created: $blocking_baseline_output" >&2
    exit 1
  fi
fi

if [[ -n "$comparison_markdown_output" ]]; then
  if [[ ! -f "$comparison_markdown_output" ]]; then
    echo "Diagnostics comparison markdown output was not created: $comparison_markdown_output" >&2
    exit 1
  fi

  if ! grep -q "## Blocking Diagnostics Regression Comparison" "$comparison_markdown_output"; then
    echo "Diagnostics comparison markdown output does not include expected heading: $comparison_markdown_output" >&2
    exit 1
  fi
fi

if [[ -n "$comparison_json_output" ]]; then
  if [[ ! -f "$comparison_json_output" ]]; then
    echo "Diagnostics comparison JSON output was not created: $comparison_json_output" >&2
    exit 1
  fi

  if ! grep -q '"effectiveExitCode"' "$comparison_json_output"; then
    echo "Diagnostics comparison JSON output does not include expected evaluation fields: $comparison_json_output" >&2
    exit 1
  fi

  if ! grep -q '"gateFailures"' "$comparison_json_output"; then
    echo "Diagnostics comparison JSON output does not include structured gate failure fields: $comparison_json_output" >&2
    exit 1
  fi

  if ! grep -Eq '"fingerprint": "[0-9a-f]{64}"' "$comparison_json_output"; then
    echo "Diagnostics comparison JSON output does not include expected fingerprint field: $comparison_json_output" >&2
    exit 1
  fi

  if ! grep -q '"fingerprintVersion": 1' "$comparison_json_output"; then
    echo "Diagnostics comparison JSON output does not include expected fingerprintVersion field: $comparison_json_output" >&2
    exit 1
  fi
fi

if [[ -n "$comparison_evaluation_markdown_output" ]]; then
  if [[ ! -f "$comparison_evaluation_markdown_output" ]]; then
    echo "Diagnostics gate evaluation markdown output was not created: $comparison_evaluation_markdown_output" >&2
    exit 1
  fi

  if ! grep -q "## Blocking Diagnostics Gate Evaluation" "$comparison_evaluation_markdown_output"; then
    echo "Diagnostics gate evaluation markdown output does not include expected heading: $comparison_evaluation_markdown_output" >&2
    exit 1
  fi

  if ! grep -Eq "Fingerprint: [0-9a-f]{64}" "$comparison_evaluation_markdown_output"; then
    echo "Diagnostics gate evaluation markdown output does not include expected fingerprint line: $comparison_evaluation_markdown_output" >&2
    exit 1
  fi

  if ! grep -q "Fingerprint Version: 1" "$comparison_evaluation_markdown_output"; then
    echo "Diagnostics gate evaluation markdown output does not include expected fingerprint version line: $comparison_evaluation_markdown_output" >&2
    exit 1
  fi
fi

if [[ -n "$comparison_json_output" && -n "$comparison_evaluation_markdown_output" ]]; then
  json_fingerprint="$(sed -nE 's/.*"fingerprint":[[:space:]]*"([0-9a-f]{64})".*/\1/p' "$comparison_json_output" | head -n1)"
  markdown_fingerprint="$(sed -nE 's/^Fingerprint: ([0-9a-f]{64})$/\1/p' "$comparison_evaluation_markdown_output" | head -n1)"

  if [[ -z "$json_fingerprint" || -z "$markdown_fingerprint" ]]; then
    echo "Diagnostics fingerprint could not be parsed for parity validation." >&2
    exit 1
  fi

  if [[ "$json_fingerprint" != "$markdown_fingerprint" ]]; then
    echo "Diagnostics fingerprint mismatch between JSON and markdown outputs." >&2
    echo "JSON fingerprint: $json_fingerprint" >&2
    echo "Markdown fingerprint: $markdown_fingerprint" >&2
    exit 1
  fi
fi

echo "Platform diagnostics report generated at $output"
