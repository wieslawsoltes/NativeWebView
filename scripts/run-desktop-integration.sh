#!/usr/bin/env bash
set -euo pipefail

configuration="Release"
no_build=false
artifacts_dir=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --no-build)
      no_build=true
      shift
      ;;
    --artifacts-dir)
      artifacts_dir="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$artifacts_dir" ]]; then
  os_name="$(uname -s | tr '[:upper:]' '[:lower:]')"
  artifacts_dir="artifacts/integration/desktop-${os_name}"
fi

mkdir -p "$artifacts_dir"

cmd=(dotnet run --project tests/NativeWebView.Integration.Desktop/NativeWebView.Integration.Desktop.csproj -c "$configuration")
if [[ "$no_build" == "true" ]]; then
  cmd+=(--no-build)
fi

set +e
output="$(NATIVEWEBVIEW_INTEGRATION_ARTIFACTS_DIR="$artifacts_dir" "${cmd[@]}" 2>&1)"
exit_code=$?
set -e

printf '%s\n' "$output"

if [[ $exit_code -ne 0 ]]; then
  echo "Desktop integration app exited with code $exit_code." >&2
  exit "$exit_code"
fi

result_line="$(grep 'NATIVEWEBVIEW_INTEGRATION_RESULT:' <<< "$output" | tail -n 1 || true)"
if [[ -z "$result_line" ]]; then
  echo "Desktop integration output did not include an integration result line." >&2
  exit 1
fi

if ! grep -q '"passed":true}$' <<< "$result_line"; then
  echo "Desktop integration result reported failure." >&2
  exit 1
fi

echo "Desktop integration passed."
