#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

configuration="Release"
output_dir="artifacts/diagnostics/exit-code-contract"
baseline="ci/baselines/blocking-issues-baseline.txt"
fingerprint_baseline=""
no_build=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --output-dir)
      output_dir="$2"
      shift 2
      ;;
    --baseline)
      baseline="$2"
      shift 2
      ;;
    --fingerprint-baseline)
      fingerprint_baseline="$2"
      shift 2
      ;;
    --no-build)
      no_build=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$baseline" ]]; then
  echo "Baseline file was not found: $baseline" >&2
  exit 1
fi

if [[ -n "$fingerprint_baseline" && ! -f "$fingerprint_baseline" ]]; then
  echo "Fingerprint baseline file was not found: $fingerprint_baseline" >&2
  exit 1
fi

mkdir -p "$output_dir"

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

empty_baseline="$temp_dir/empty-baseline.txt"
stale_baseline="$temp_dir/stale-baseline.txt"
pass_baseline="$temp_dir/pass-baseline.txt"
preflight_report="$temp_dir/preflight-report.json"

cat > "$empty_baseline" <<'BASELINE'
# NativeWebView blocking diagnostics baseline
# Format: <Platform>|<IssueCode>
BASELINE

cat > "$stale_baseline" <<'BASELINE'
# NativeWebView blocking diagnostics baseline
# Format: <Platform>|<IssueCode>
Windows|windows.error.fake
BASELINE

summary_markdown="$output_dir/exit-code-contract-summary.md"
summary_csv="$output_dir/exit-code-contract-summary.csv"
summary_json="$output_dir/exit-code-contract-summary.json"

fingerprint_baseline_enabled="false"
fingerprint_baseline_all_matched="null"
fingerprint_baseline_mismatch_count="null"
fingerprint_baseline_has_mismatch=false

cat > "$summary_csv" <<'CSV'
case,expected_exit_code,actual_exit_code,pass,log_file,evaluation_json,evaluation_markdown
CSV

run_case() {
  local case_name="$1"
  local expected_exit_code="$2"
  local evaluation_json="$3"
  local evaluation_markdown="$4"
  shift 4

  local log_file="$output_dir/${case_name}.log"

  set +e
  "${contract_env[@]}" "$repo_root/scripts/run-platform-diagnostics-report.sh" "$@" > "$log_file" 2>&1
  local actual_exit_code=$?
  set -e

  local pass="false"
  if [[ "$actual_exit_code" -eq "$expected_exit_code" ]]; then
    pass="true"
  fi

  printf '%s,%s,%s,%s,%s,%s,%s\n' \
    "$case_name" \
    "$expected_exit_code" \
    "$actual_exit_code" \
    "$pass" \
    "$log_file" \
    "$evaluation_json" \
    "$evaluation_markdown" >> "$summary_csv"

  if [[ "$pass" != "true" ]]; then
    echo "Exit code contract case '$case_name' failed: expected $expected_exit_code, got $actual_exit_code." >&2
    echo "--- $log_file ---" >&2
    cat "$log_file" >&2
    exit 1
  fi
}

get_markdown_fingerprint() {
  local markdown_file="$1"
  local parsed_fingerprint
  parsed_fingerprint="$(sed -nE 's/^Fingerprint: ([0-9a-f]{64})$/\1/p' "$markdown_file" | head -n1)"
  if [[ -z "$parsed_fingerprint" ]]; then
    return 1
  fi

  printf '%s\n' "$parsed_fingerprint"
}

json_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/\\n}"
  value="${value//$'\r'/\\r}"
  value="${value//$'\t'/\\t}"
  printf '%s' "$value"
}

common_args=(--configuration "$configuration" --platform all)
if [[ "$no_build" == "true" ]]; then
  common_args+=(--no-build)
fi

contract_host_platform_override="${NATIVEWEBVIEW_DIAGNOSTICS_HOST_PLATFORM_OVERRIDE:-Unknown}"
contract_env=(
  env
  "NATIVEWEBVIEW_DIAGNOSTICS_HOST_PLATFORM=$contract_host_platform_override"
)

"${contract_env[@]}" "$repo_root/scripts/run-platform-diagnostics-report.sh" \
  "${common_args[@]}" \
  --output "$preflight_report" \
  --blocking-baseline-output "$pass_baseline" \
  --allow-not-ready > "$output_dir/preflight.log" 2>&1

if [[ ! -f "$pass_baseline" ]]; then
  echo "Pass baseline generation failed: $pass_baseline" >&2
  exit 1
fi

run_case \
  pass \
  0 \
  "$output_dir/pass-regression.json" \
  "$output_dir/pass-gate-evaluation.md" \
  "${common_args[@]}" \
  --output "$output_dir/pass-report.json" \
  --markdown-output "$output_dir/pass-report.md" \
  --blocking-baseline "$pass_baseline" \
  --comparison-markdown-output "$output_dir/pass-regression.md" \
  --comparison-json-output "$output_dir/pass-regression.json" \
  --comparison-evaluation-markdown-output "$output_dir/pass-gate-evaluation.md" \
  --require-baseline-sync \
  --allow-not-ready

run_case \
  require-ready \
  10 \
  "" \
  "$output_dir/require-ready-gate-evaluation.md" \
  "${common_args[@]}" \
  --output "$output_dir/require-ready-report.json" \
  --comparison-evaluation-markdown-output "$output_dir/require-ready-gate-evaluation.md" \
  --warnings-as-errors

run_case \
  regression \
  11 \
  "$output_dir/regression-evaluation.json" \
  "$output_dir/regression-gate-evaluation.md" \
  "${common_args[@]}" \
  --output "$output_dir/regression-report.json" \
  --blocking-baseline "$empty_baseline" \
  --comparison-json-output "$output_dir/regression-evaluation.json" \
  --comparison-evaluation-markdown-output "$output_dir/regression-gate-evaluation.md" \
  --warnings-as-errors \
  --allow-not-ready

run_case \
  baseline-sync \
  12 \
  "$output_dir/baseline-sync-evaluation.json" \
  "$output_dir/baseline-sync-gate-evaluation.md" \
  "${common_args[@]}" \
  --output "$output_dir/baseline-sync-report.json" \
  --blocking-baseline "$stale_baseline" \
  --comparison-json-output "$output_dir/baseline-sync-evaluation.json" \
  --comparison-evaluation-markdown-output "$output_dir/baseline-sync-gate-evaluation.md" \
  --allow-regression \
  --require-baseline-sync \
  --allow-not-ready

run_case \
  multi-gate \
  13 \
  "$output_dir/multi-gate-evaluation.json" \
  "$output_dir/multi-gate-gate-evaluation.md" \
  "${common_args[@]}" \
  --output "$output_dir/multi-gate-report.json" \
  --blocking-baseline "$stale_baseline" \
  --comparison-json-output "$output_dir/multi-gate-evaluation.json" \
  --comparison-evaluation-markdown-output "$output_dir/multi-gate-gate-evaluation.md" \
  --warnings-as-errors \
  --require-baseline-sync

for required in \
  "$output_dir/pass-regression.json" \
  "$output_dir/regression-evaluation.json" \
  "$output_dir/baseline-sync-evaluation.json" \
  "$output_dir/multi-gate-evaluation.json"; do
  if [[ ! -f "$required" ]]; then
    echo "Expected evaluation JSON was not created: $required" >&2
    exit 1
  fi

  if ! grep -q '"effectiveExitCode"' "$required"; then
    echo "Evaluation JSON does not include effectiveExitCode: $required" >&2
    exit 1
  fi

  if ! grep -q '"gateFailures"' "$required"; then
    echo "Evaluation JSON does not include gateFailures: $required" >&2
    exit 1
  fi

  if ! grep -Eq '"fingerprint": "[0-9a-f]{64}"' "$required"; then
    echo "Evaluation JSON does not include a valid fingerprint field: $required" >&2
    exit 1
  fi

  if ! grep -q '"fingerprintVersion": 1' "$required"; then
    echo "Evaluation JSON does not include expected fingerprintVersion field: $required" >&2
    exit 1
  fi
done

for required in \
  "$output_dir/pass-gate-evaluation.md" \
  "$output_dir/require-ready-gate-evaluation.md" \
  "$output_dir/regression-gate-evaluation.md" \
  "$output_dir/baseline-sync-gate-evaluation.md" \
  "$output_dir/multi-gate-gate-evaluation.md"; do
  if [[ ! -f "$required" ]]; then
    echo "Expected evaluation markdown was not created: $required" >&2
    exit 1
  fi

  if ! grep -q "## Blocking Diagnostics Gate Evaluation" "$required"; then
    echo "Evaluation markdown does not include expected heading: $required" >&2
    exit 1
  fi

  if ! grep -Eq "Fingerprint: [0-9a-f]{64}" "$required"; then
    echo "Evaluation markdown does not include a valid fingerprint line: $required" >&2
    exit 1
  fi

  if ! grep -q "Fingerprint Version: 1" "$required"; then
    echo "Evaluation markdown does not include expected fingerprint version line: $required" >&2
    exit 1
  fi
done

for pair in \
  "pass-regression.json:pass-gate-evaluation.md" \
  "regression-evaluation.json:regression-gate-evaluation.md" \
  "baseline-sync-evaluation.json:baseline-sync-gate-evaluation.md" \
  "multi-gate-evaluation.json:multi-gate-gate-evaluation.md"; do
  json_file="$output_dir/${pair%%:*}"
  markdown_file="$output_dir/${pair##*:}"

  json_fingerprint="$(sed -nE 's/.*"fingerprint":[[:space:]]*"([0-9a-f]{64})".*/\1/p' "$json_file" | head -n1)"
  markdown_fingerprint="$(sed -nE 's/^Fingerprint: ([0-9a-f]{64})$/\1/p' "$markdown_file" | head -n1)"

  if [[ -z "$json_fingerprint" || -z "$markdown_fingerprint" ]]; then
    echo "Could not parse fingerprint for parity check: $json_file and $markdown_file" >&2
    exit 1
  fi

  if [[ "$json_fingerprint" != "$markdown_fingerprint" ]]; then
    echo "Fingerprint mismatch between JSON and markdown outputs." >&2
    echo "JSON file: $json_file ($json_fingerprint)" >&2
    echo "Markdown file: $markdown_file ($markdown_fingerprint)" >&2
    exit 1
  fi
done

pass_fingerprint="$(get_markdown_fingerprint "$output_dir/pass-gate-evaluation.md")" || {
  echo "Could not parse fingerprint from markdown file: $output_dir/pass-gate-evaluation.md" >&2
  exit 1
}
require_ready_fingerprint="$(get_markdown_fingerprint "$output_dir/require-ready-gate-evaluation.md")" || {
  echo "Could not parse fingerprint from markdown file: $output_dir/require-ready-gate-evaluation.md" >&2
  exit 1
}
regression_fingerprint="$(get_markdown_fingerprint "$output_dir/regression-gate-evaluation.md")" || {
  echo "Could not parse fingerprint from markdown file: $output_dir/regression-gate-evaluation.md" >&2
  exit 1
}
baseline_sync_fingerprint="$(get_markdown_fingerprint "$output_dir/baseline-sync-gate-evaluation.md")" || {
  echo "Could not parse fingerprint from markdown file: $output_dir/baseline-sync-gate-evaluation.md" >&2
  exit 1
}
multi_gate_fingerprint="$(get_markdown_fingerprint "$output_dir/multi-gate-gate-evaluation.md")" || {
  echo "Could not parse fingerprint from markdown file: $output_dir/multi-gate-gate-evaluation.md" >&2
  exit 1
}

fingerprint_current="$output_dir/fingerprint-current.txt"
{
  echo "# NativeWebView diagnostics fingerprint baseline"
  echo "# Format: <Scenario>|<Fingerprint>"
  echo "pass|$pass_fingerprint"
  echo "require-ready|$require_ready_fingerprint"
  echo "regression|$regression_fingerprint"
  echo "baseline-sync|$baseline_sync_fingerprint"
  echo "multi-gate|$multi_gate_fingerprint"
} > "$fingerprint_current"

fingerprint_comparison_markdown="$output_dir/fingerprint-baseline-comparison.md"
fingerprint_comparison_json="$output_dir/fingerprint-baseline-comparison.json"

if [[ -n "$fingerprint_baseline" ]]; then
  expected_pass=""
  expected_require_ready=""
  expected_regression=""
  expected_baseline_sync=""
  expected_multi_gate=""

  while IFS= read -r raw_line || [[ -n "$raw_line" ]]; do
    line="$(echo "$raw_line" | tr -d '\r')"
    if [[ -z "$line" || "$line" == \#* ]]; then
      continue
    fi

    if [[ "${line%%|*}" == "$line" ]]; then
      echo "Invalid fingerprint baseline line (missing '|'): $line" >&2
      exit 1
    fi

    baseline_case="${line%%|*}"
    baseline_fingerprint="${line#*|}"

    case "$baseline_case" in
      pass)
        if [[ -n "$expected_pass" ]]; then
          echo "Duplicate scenario in fingerprint baseline: $baseline_case" >&2
          exit 1
        fi
        expected_pass="$baseline_fingerprint"
        ;;
      require-ready)
        if [[ -n "$expected_require_ready" ]]; then
          echo "Duplicate scenario in fingerprint baseline: $baseline_case" >&2
          exit 1
        fi
        expected_require_ready="$baseline_fingerprint"
        ;;
      regression)
        if [[ -n "$expected_regression" ]]; then
          echo "Duplicate scenario in fingerprint baseline: $baseline_case" >&2
          exit 1
        fi
        expected_regression="$baseline_fingerprint"
        ;;
      baseline-sync)
        if [[ -n "$expected_baseline_sync" ]]; then
          echo "Duplicate scenario in fingerprint baseline: $baseline_case" >&2
          exit 1
        fi
        expected_baseline_sync="$baseline_fingerprint"
        ;;
      multi-gate)
        if [[ -n "$expected_multi_gate" ]]; then
          echo "Duplicate scenario in fingerprint baseline: $baseline_case" >&2
          exit 1
        fi
        expected_multi_gate="$baseline_fingerprint"
        ;;
      *)
        echo "Unsupported scenario in fingerprint baseline: $baseline_case" >&2
        exit 1
        ;;
    esac

    if ! [[ "$baseline_fingerprint" =~ ^[0-9a-f]{64}$ ]]; then
      echo "Invalid fingerprint format in baseline for $baseline_case: $baseline_fingerprint" >&2
      exit 1
    fi
  done < "$fingerprint_baseline"

  if [[ -z "$expected_pass" || -z "$expected_require_ready" || -z "$expected_regression" || -z "$expected_baseline_sync" || -z "$expected_multi_gate" ]]; then
    echo "Fingerprint baseline is missing one or more required scenarios." >&2
    exit 1
  fi

  pass_match="false"
  require_ready_match="false"
  regression_match="false"
  baseline_sync_match="false"
  multi_gate_match="false"

  if [[ "$pass_fingerprint" == "$expected_pass" ]]; then
    pass_match="true"
  fi

  if [[ "$require_ready_fingerprint" == "$expected_require_ready" ]]; then
    require_ready_match="true"
  fi

  if [[ "$regression_fingerprint" == "$expected_regression" ]]; then
    regression_match="true"
  fi

  if [[ "$baseline_sync_fingerprint" == "$expected_baseline_sync" ]]; then
    baseline_sync_match="true"
  fi

  if [[ "$multi_gate_fingerprint" == "$expected_multi_gate" ]]; then
    multi_gate_match="true"
  fi

  mismatch_count=0
  for match_value in \
    "$pass_match" \
    "$require_ready_match" \
    "$regression_match" \
    "$baseline_sync_match" \
    "$multi_gate_match"; do
    if [[ "$match_value" != "true" ]]; then
      mismatch_count=$((mismatch_count + 1))
    fi
  done

  all_matched="true"
  if [[ "$mismatch_count" -gt 0 ]]; then
    all_matched="false"
  fi

  fingerprint_baseline_enabled="true"
  fingerprint_baseline_all_matched="$all_matched"
  fingerprint_baseline_mismatch_count="$mismatch_count"

  baseline_path_json="$(json_escape "$fingerprint_baseline")"
  current_path_json="$(json_escape "$fingerprint_current")"
  cat > "$fingerprint_comparison_json" <<JSON
{
  "baselinePath": "$baseline_path_json",
  "currentPath": "$current_path_json",
  "allMatched": $all_matched,
  "mismatchCount": $mismatch_count,
  "scenarios": [
    {
      "name": "pass",
      "expected": "$expected_pass",
      "actual": "$pass_fingerprint",
      "matched": $pass_match
    },
    {
      "name": "require-ready",
      "expected": "$expected_require_ready",
      "actual": "$require_ready_fingerprint",
      "matched": $require_ready_match
    },
    {
      "name": "regression",
      "expected": "$expected_regression",
      "actual": "$regression_fingerprint",
      "matched": $regression_match
    },
    {
      "name": "baseline-sync",
      "expected": "$expected_baseline_sync",
      "actual": "$baseline_sync_fingerprint",
      "matched": $baseline_sync_match
    },
    {
      "name": "multi-gate",
      "expected": "$expected_multi_gate",
      "actual": "$multi_gate_fingerprint",
      "matched": $multi_gate_match
    }
  ]
}
JSON

  {
    echo "## Diagnostics Fingerprint Baseline Comparison"
    echo
    echo "Baseline: \`$fingerprint_baseline\`"
    echo "Current: \`$fingerprint_current\`"
    echo "All Matched: $all_matched"
    echo "Mismatch Count: $mismatch_count"
    echo
    echo "| Scenario | Expected | Actual | Matched |"
    echo "| --- | --- | --- | --- |"
    printf '| %s | %s | %s | %s |\n' "pass" "$expected_pass" "$pass_fingerprint" "$pass_match"
    printf '| %s | %s | %s | %s |\n' "require-ready" "$expected_require_ready" "$require_ready_fingerprint" "$require_ready_match"
    printf '| %s | %s | %s | %s |\n' "regression" "$expected_regression" "$regression_fingerprint" "$regression_match"
    printf '| %s | %s | %s | %s |\n' "baseline-sync" "$expected_baseline_sync" "$baseline_sync_fingerprint" "$baseline_sync_match"
    printf '| %s | %s | %s | %s |\n' "multi-gate" "$expected_multi_gate" "$multi_gate_fingerprint" "$multi_gate_match"
  } > "$fingerprint_comparison_markdown"

  if [[ "$mismatch_count" -gt 0 ]]; then
    echo "Fingerprint baseline mismatch detected in $mismatch_count scenario(s)." >&2
    if [[ "$pass_match" != "true" ]]; then
      echo "  pass: expected $expected_pass, actual $pass_fingerprint" >&2
    fi
    if [[ "$require_ready_match" != "true" ]]; then
      echo "  require-ready: expected $expected_require_ready, actual $require_ready_fingerprint" >&2
    fi
    if [[ "$regression_match" != "true" ]]; then
      echo "  regression: expected $expected_regression, actual $regression_fingerprint" >&2
    fi
    if [[ "$baseline_sync_match" != "true" ]]; then
      echo "  baseline-sync: expected $expected_baseline_sync, actual $baseline_sync_fingerprint" >&2
    fi
    if [[ "$multi_gate_match" != "true" ]]; then
      echo "  multi-gate: expected $expected_multi_gate, actual $multi_gate_fingerprint" >&2
    fi
    echo "Fingerprint comparison markdown: $fingerprint_comparison_markdown" >&2
    echo "Fingerprint comparison json: $fingerprint_comparison_json" >&2
    echo "Current fingerprint file: $fingerprint_current" >&2
    fingerprint_baseline_has_mismatch=true
  fi
fi

if ! grep -q '"effectiveExitCode": 11' "$output_dir/regression-evaluation.json"; then
  echo "Regression evaluation JSON did not contain expected exit code 11." >&2
  exit 1
fi

if ! grep -q '"kind": "Regression"' "$output_dir/regression-evaluation.json" || \
   ! grep -q '"exitCode": 11' "$output_dir/regression-evaluation.json"; then
  echo "Regression evaluation JSON did not contain expected structured gate failure entry." >&2
  exit 1
fi

if ! grep -q '"effectiveExitCode": 12' "$output_dir/baseline-sync-evaluation.json"; then
  echo "Baseline-sync evaluation JSON did not contain expected exit code 12." >&2
  exit 1
fi

if ! grep -q '"kind": "BaselineSync"' "$output_dir/baseline-sync-evaluation.json" || \
   ! grep -q '"exitCode": 12' "$output_dir/baseline-sync-evaluation.json"; then
  echo "Baseline-sync evaluation JSON did not contain expected structured gate failure entry." >&2
  exit 1
fi

if ! grep -q '"effectiveExitCode": 13' "$output_dir/multi-gate-evaluation.json"; then
  echo "Multi-gate evaluation JSON did not contain expected exit code 13." >&2
  exit 1
fi

if ! grep -q '"RequireReady"' "$output_dir/multi-gate-evaluation.json" || \
   ! grep -q '"Regression"' "$output_dir/multi-gate-evaluation.json" || \
   ! grep -q '"BaselineSync"' "$output_dir/multi-gate-evaluation.json"; then
  echo "Multi-gate evaluation JSON did not contain expected failing gate names." >&2
  exit 1
fi

if ! grep -q '"Fix blocking diagnostics issues or run with --allow-not-ready when collecting non-gating reports."' "$output_dir/multi-gate-evaluation.json" || \
   ! grep -q '"Resolve newly introduced blocking issues or run with --allow-regression when triaging intentional changes."' "$output_dir/multi-gate-evaluation.json" || \
   ! grep -q '"Refresh baseline using ./scripts/update-blocking-baseline.sh when resolved entries are intentional."' "$output_dir/multi-gate-evaluation.json"; then
  echo "Multi-gate evaluation JSON did not contain expected gate failure recommendations." >&2
  exit 1
fi

if ! grep -q "Effective Exit Code: 0" "$output_dir/pass-gate-evaluation.md"; then
  echo "Pass gate evaluation markdown did not contain expected exit code 0." >&2
  exit 1
fi

if ! grep -q "Effective Exit Code: 10" "$output_dir/require-ready-gate-evaluation.md"; then
  echo "Require-ready gate evaluation markdown did not contain expected exit code 10." >&2
  exit 1
fi

if ! grep -q "Effective Exit Code: 11" "$output_dir/regression-gate-evaluation.md"; then
  echo "Regression gate evaluation markdown did not contain expected exit code 11." >&2
  exit 1
fi

if ! grep -q "Effective Exit Code: 12" "$output_dir/baseline-sync-gate-evaluation.md"; then
  echo "Baseline-sync gate evaluation markdown did not contain expected exit code 12." >&2
  exit 1
fi

if ! grep -q "Effective Exit Code: 13" "$output_dir/multi-gate-gate-evaluation.md"; then
  echo "Multi-gate evaluation markdown did not contain expected exit code 13." >&2
  exit 1
fi

{
  echo "## Diagnostics Exit Code Contract Validation"
  echo
  echo "| Case | Expected | Actual | Pass | Fingerprint |"
  echo "| --- | --- | --- | --- | --- |"
  tail -n +2 "$summary_csv" | while IFS=',' read -r case_name expected actual pass _log _json markdown_file; do
    fingerprint="n/a"
    if [[ -f "$markdown_file" ]]; then
      parsed_fingerprint="$(sed -nE 's/^Fingerprint: ([0-9a-f]{64})$/\1/p' "$markdown_file" | head -n1)"
      if [[ -n "$parsed_fingerprint" ]]; then
        fingerprint="$parsed_fingerprint"
      fi
    fi
    printf '| %s | %s | %s | %s | %s |\n' "$case_name" "$expected" "$actual" "$pass" "$fingerprint"
  done
} > "$summary_markdown"

summary_json_cases="$temp_dir/exit-code-contract-summary-cases.json"
all_passed="true"
case_count=0
first_case="true"

: > "$summary_json_cases"
while IFS=',' read -r case_name expected actual pass log_file evaluation_json_file markdown_file; do
  case_count=$((case_count + 1))

  if [[ "$pass" != "true" ]]; then
    all_passed="false"
  fi

  case_name_json="$(json_escape "$case_name")"
  log_file_json="$(json_escape "$log_file")"
  markdown_file_json="$(json_escape "$markdown_file")"

  if [[ -n "$evaluation_json_file" ]]; then
    evaluation_json_value="\"$(json_escape "$evaluation_json_file")\""
  else
    evaluation_json_value="null"
  fi

  fingerprint_value="null"
  if [[ -f "$markdown_file" ]]; then
    parsed_fingerprint="$(sed -nE 's/^Fingerprint: ([0-9a-f]{64})$/\1/p' "$markdown_file" | head -n1)"
    if [[ -n "$parsed_fingerprint" ]]; then
      fingerprint_value="\"$parsed_fingerprint\""
    fi
  fi

  if [[ "$first_case" == "true" ]]; then
    first_case="false"
  else
    printf ',\n' >> "$summary_json_cases"
  fi

  {
    printf '    {\n'
    printf '      "name": "%s",\n' "$case_name_json"
    printf '      "expectedExitCode": %s,\n' "$expected"
    printf '      "actualExitCode": %s,\n' "$actual"
    printf '      "passed": %s,\n' "$pass"
    printf '      "fingerprint": %s,\n' "$fingerprint_value"
    printf '      "logFile": "%s",\n' "$log_file_json"
    printf '      "evaluationJson": %s,\n' "$evaluation_json_value"
    printf '      "evaluationMarkdown": "%s"\n' "$markdown_file_json"
    printf '    }'
  } >> "$summary_json_cases"
done < <(tail -n +2 "$summary_csv")

if [[ "$case_count" -eq 0 ]]; then
  echo "Exit code contract summary did not include any scenario cases." >&2
  exit 1
fi

fingerprint_current_json="$(json_escape "$fingerprint_current")"
if [[ -n "$fingerprint_baseline" ]]; then
  fingerprint_baseline_path_json="$(json_escape "$fingerprint_baseline")"
  fingerprint_comparison_markdown_json="$(json_escape "$fingerprint_comparison_markdown")"
  fingerprint_comparison_json_path_json="$(json_escape "$fingerprint_comparison_json")"
else
  fingerprint_baseline_path_json=""
  fingerprint_comparison_markdown_json=""
  fingerprint_comparison_json_path_json=""
fi

cat > "$summary_json" <<JSON
{
  "allPassed": $all_passed,
  "caseCount": $case_count,
  "cases": [
$(cat "$summary_json_cases")
  ],
  "fingerprintBaseline": {
    "enabled": $fingerprint_baseline_enabled,
    "baselinePath": $(if [[ -n "$fingerprint_baseline" ]]; then printf '"%s"' "$fingerprint_baseline_path_json"; else printf 'null'; fi),
    "currentPath": "$fingerprint_current_json",
    "allMatched": $fingerprint_baseline_all_matched,
    "mismatchCount": $fingerprint_baseline_mismatch_count,
    "comparisonMarkdown": $(if [[ -n "$fingerprint_baseline" ]]; then printf '"%s"' "$fingerprint_comparison_markdown_json"; else printf 'null'; fi),
    "comparisonJson": $(if [[ -n "$fingerprint_baseline" ]]; then printf '"%s"' "$fingerprint_comparison_json_path_json"; else printf 'null'; fi)
  }
}
JSON

if [[ ! -f "$summary_json" ]]; then
  echo "Summary JSON was not created: $summary_json" >&2
  exit 1
fi

if ! grep -q '"allPassed"' "$summary_json" || \
   ! grep -q '"caseCount"' "$summary_json" || \
   ! grep -q '"fingerprintBaseline"' "$summary_json"; then
  echo "Summary JSON is missing required contract fields: $summary_json" >&2
  exit 1
fi

if [[ "$fingerprint_baseline_has_mismatch" == "true" ]]; then
  echo "Exit code contract scenarios passed, but fingerprint baseline drift was detected." >&2
  echo "Summary markdown: $summary_markdown" >&2
  echo "Summary csv: $summary_csv" >&2
  echo "Summary json: $summary_json" >&2
  exit 1
fi

echo "Diagnostics exit code contract validation passed."
echo "Summary markdown: $summary_markdown"
echo "Summary csv: $summary_csv"
echo "Summary json: $summary_json"
echo "Current fingerprint baseline: $fingerprint_current"
if [[ -n "$fingerprint_baseline" ]]; then
  echo "Fingerprint comparison markdown: $fingerprint_comparison_markdown"
  echo "Fingerprint comparison json: $fingerprint_comparison_json"
fi
