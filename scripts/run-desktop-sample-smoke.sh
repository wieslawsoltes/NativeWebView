#!/usr/bin/env bash
set -euo pipefail

configuration="Release"
no_build=false

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
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

cmd=(dotnet run --project samples/NativeWebView.Sample.Desktop/NativeWebView.Sample.Desktop.csproj -c "$configuration" --framework "${NATIVEWEBVIEW_FRAMEWORK:-net11.0}")
if [[ "$no_build" == "true" ]]; then
  cmd+=(--no-build)
fi
cmd+=(-- --smoke)

set +e
output="$(NATIVEWEBVIEW_DIAGNOSTICS_REQUIRE_READY=1 "${cmd[@]}" 2>&1)"
exit_code=$?
set -e
printf '%s\n' "$output"

if [[ $exit_code -ne 0 ]]; then
  echo "Desktop sample command failed with exit code $exit_code." >&2
  exit "$exit_code"
fi

if ! grep -q "Desktop backend matrix" <<< "$output"; then
  echo "Desktop sample output did not include matrix header." >&2
  exit 1
fi

if ! grep -q "Result: 27/27 checks passed." <<< "$output"; then
  echo "Desktop sample smoke checks did not report 27/27 success." >&2
  exit 1
fi

echo "Desktop sample smoke passed."
