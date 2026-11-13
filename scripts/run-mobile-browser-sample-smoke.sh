#!/usr/bin/env bash
set -euo pipefail

configuration="Release"
platform="all"
no_build=false

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

cmd=(dotnet run --project samples/NativeWebView.Sample.MobileBrowser/NativeWebView.Sample.MobileBrowser.csproj -c "$configuration" --framework "${NATIVEWEBVIEW_FRAMEWORK:-net11.0}")
if [[ "$no_build" == "true" ]]; then
  cmd+=(--no-build)
fi

set +e
output="$(NATIVEWEBVIEW_DIAGNOSTICS_REQUIRE_READY=1 "${cmd[@]}" 2>&1)"
exit_code=$?
set -e
printf '%s\n' "$output"

if [[ $exit_code -ne 0 ]]; then
  echo "Mobile/browser sample command failed with exit code $exit_code." >&2
  exit "$exit_code"
fi

require_line() {
  local pattern="$1"
  local message="$2"
  if ! grep -q "$pattern" <<< "$output"; then
    echo "$message" >&2
    exit 1
  fi
}

case "$platform" in
  all)
    require_line "Mobile/browser matrix for IOS:" "Missing iOS matrix section."
    require_line "Mobile/browser matrix for Android:" "Missing Android matrix section."
    require_line "Mobile/browser matrix for Browser:" "Missing Browser matrix section."
    require_line "Result: 54/54 checks passed." "Mobile/browser sample did not report 54/54 success."
    ;;
  ios)
    require_line "Mobile/browser matrix for IOS:" "Missing iOS matrix section."
    require_line "\\[PASS\\] Authenticate interactive" "Missing iOS authenticate interactive pass marker."
    require_line "Result: 54/54 checks passed." "Mobile/browser sample did not report 54/54 success."
    ;;
  android)
    require_line "Mobile/browser matrix for Android:" "Missing Android matrix section."
    require_line "\\[PASS\\] Authenticate interactive" "Missing Android authenticate interactive pass marker."
    require_line "Result: 54/54 checks passed." "Mobile/browser sample did not report 54/54 success."
    ;;
  browser)
    require_line "Mobile/browser matrix for Browser:" "Missing Browser matrix section."
    require_line "\\[PASS\\] Authenticate interactive" "Missing Browser authenticate interactive pass marker."
    require_line "Result: 54/54 checks passed." "Mobile/browser sample did not report 54/54 success."
    ;;
  *)
    echo "Unsupported platform selector: $platform" >&2
    exit 1
    ;;
esac

echo "Mobile/browser sample smoke passed for platform selector '$platform'."
