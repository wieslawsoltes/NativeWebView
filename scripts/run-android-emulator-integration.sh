#!/usr/bin/env bash
set -euo pipefail

configuration="Release"
no_build=false
logcat_output="artifacts/integration/android/logcat.txt"
adb_wait_timeout_seconds="${ADB_WAIT_TIMEOUT_SECONDS:-180}"
integration_timeout_seconds="${ANDROID_INTEGRATION_TIMEOUT_SECONDS:-240}"
project_path="tests/NativeWebView.Integration.Android/NativeWebView.Integration.Android.csproj"
framework="net8.0-android34.0"
package_name="com.wieslawsoltes.nativewebview.integration.android"
log_tag="NWVIntegration"

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
    --logcat-output)
      logcat_output="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

mkdir -p "$(dirname "$logcat_output")"

if ! command -v adb >/dev/null 2>&1; then
  echo "adb is required for Android integration." >&2
  exit 1
fi

deadline=$((SECONDS + adb_wait_timeout_seconds))
while true; do
  state="$(adb get-state 2>/dev/null || true)"
  if [[ "$state" == "device" ]]; then
    break
  fi

  if (( SECONDS >= deadline )); then
    echo "Timed out waiting for adb device after ${adb_wait_timeout_seconds}s." >&2
    exit 1
  fi

  sleep 2
done

abi="$(adb shell getprop ro.product.cpu.abi 2>/dev/null | tr -d '\r')"
runtime_id="android-x64"
case "$abi" in
  arm64-v8a)
    runtime_id="android-arm64"
    ;;
  x86_64|"")
    runtime_id="android-x64"
    ;;
esac

if [[ "$no_build" != "true" ]]; then
  dotnet build "$project_path" \
    -c "$configuration" \
    -f "$framework" \
    -p:RuntimeIdentifier="$runtime_id" \
    -p:AndroidPackageFormat=apk
fi

apk_path=""
while IFS= read -r candidate; do
  apk_path="$candidate"
  break
done < <(find "tests/NativeWebView.Integration.Android/bin/${configuration}" -type f \( -name '*-Signed.apk' -o -name '*.apk' \) | sort)

if [[ -z "$apk_path" ]]; then
  echo "Unable to locate built Android APK." >&2
  exit 1
fi

adb uninstall "$package_name" >/dev/null 2>&1 || true
adb install -r "$apk_path" >/dev/null
adb logcat -c
adb shell am force-stop "$package_name" >/dev/null 2>&1 || true

launcher_component="$(adb shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER "$package_name" 2>/dev/null | tr -d '\r' | tail -n 1 || true)"
if [[ "$launcher_component" == */* ]]; then
  adb shell am start -W -n "$launcher_component" >/dev/null
else
  adb shell monkey -p "$package_name" -c android.intent.category.LAUNCHER 1 >/dev/null
fi

deadline=$((SECONDS + integration_timeout_seconds))
while (( SECONDS < deadline )); do
  adb logcat -d -s "${log_tag}:I" > "$logcat_output" || true

  result_line="$(grep 'NATIVEWEBVIEW_INTEGRATION_RESULT:' "$logcat_output" | tail -n 1 || true)"
  if [[ -n "$result_line" ]]; then
    printf '%s\n' "$result_line"
    adb shell am force-stop "$package_name" >/dev/null 2>&1 || true

    if grep -q '"passed":true}$' <<< "$result_line"; then
      echo "Android emulator integration passed."
      exit 0
    fi

    echo "Android emulator integration reported failure." >&2
    exit 1
  fi

  sleep 2
done

adb shell am force-stop "$package_name" >/dev/null 2>&1 || true
echo "Timed out waiting for Android integration result after ${integration_timeout_seconds}s." >&2
exit 1
